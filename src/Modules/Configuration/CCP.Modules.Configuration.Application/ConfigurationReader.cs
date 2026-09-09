using System.Globalization;
using CCP.Modules.Configuration.Application.Abstractions;
using CCP.Modules.Configuration.Domain.Flags;
using CCP.Modules.Configuration.Domain.Settings;

namespace CCP.Modules.Configuration.Application;

/// <summary>
/// Reads settings and evaluates flags, from a snapshot cached on a version
/// stamp.
/// <para>
/// <b>The correctness property that matters: a change takes effect on the very
/// next request.</b> That is why the cache is keyed on a stamp held in the
/// database rather than on a time-to-live. A time-to-live leaves a window —
/// however short — in which a capability somebody deliberately switched off is
/// still on, and "however short" is not a property anyone can reason about while
/// deciding whether to switch it off.
/// </para>
/// <para>
/// The cost is one indexed single-row read to fetch the stamp. That is far
/// cheaper than reloading the configuration each time, and unlike a time-to-live
/// it is exactly correct. It is the same mechanism the permission resolver uses,
/// deliberately: two caching strategies in one Platform is one more thing to
/// reason about during an incident.
/// </para>
/// </summary>
public sealed class ConfigurationReader(
    IConfigurationRepository repository,
    IConfigurationVersionStore versionStore,
    IConfigurationCache cache) : IConfigurationReader
{
    public async Task<string?> GetAsync(
        string key,
        Guid? companyId = null,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ConfigurationSnapshot snapshot = await LoadAsync(cancellationToken);

        string normalised = key.Trim().ToLowerInvariant();

        if (!snapshot.Definitions.TryGetValue(normalised, out SettingDefinition? definition))
        {
            // Never declared. Distinct from "declared and unset", because one is
            // a working setting at its default and the other is a typo.
            return null;
        }

        return Resolve(snapshot, normalised, companyId, applicationId) ?? definition.DefaultValue;
    }

    /// <summary>
    /// Narrowest wins.
    /// <para>
    /// Application, then company, then Platform. The only rule anybody can hold
    /// in their head — and the alternative, where something broader can override
    /// something narrower, produces the case where changing a Platform default
    /// silently undoes a deliberate local decision.
    /// </para>
    /// </summary>
    private static string? Resolve(
        ConfigurationSnapshot snapshot, string key, Guid? companyId, Guid? applicationId)
    {
        if (!snapshot.Values.TryGetValue(key, out IReadOnlyList<SettingValue>? values))
        {
            return null;
        }

        if (applicationId is { } application)
        {
            string? scoped = values.FirstOrDefault(
                v => v.Scope == SettingScope.Application && v.ScopeId == application)?.Value;

            if (scoped is not null)
            {
                return scoped;
            }
        }

        if (companyId is { } company)
        {
            string? scoped = values.FirstOrDefault(
                v => v.Scope == SettingScope.Company && v.ScopeId == company)?.Value;

            if (scoped is not null)
            {
                return scoped;
            }
        }

        return values.FirstOrDefault(v => v.Scope == SettingScope.Platform)?.Value;
    }

    public async Task<bool> GetBooleanAsync(
        string key, bool fallback = false, CancellationToken cancellationToken = default)
    {
        string? value = await GetAsync(key, cancellationToken: cancellationToken);

        // The fallback covers an undeclared key, which is a programming mistake
        // rather than a configuration one. A stored value that will not parse
        // cannot happen: the definition validated it before it was stored.
        return value is not null && bool.TryParse(value, out bool parsed) ? parsed : fallback;
    }

    public async Task<int> GetIntegerAsync(
        string key, int fallback = 0, CancellationToken cancellationToken = default)
    {
        string? value = await GetAsync(key, cancellationToken: cancellationToken);

        return value is not null
               && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    public async Task<TimeSpan> GetDurationAsync(
        string key, TimeSpan fallback = default, CancellationToken cancellationToken = default)
    {
        string? value = await GetAsync(key, cancellationToken: cancellationToken);

        return value is not null
               && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed)
            ? parsed
            : fallback;
    }

    public async Task<bool> IsFeatureOnAsync(
        string key,
        IReadOnlyList<Guid> callerRoleIds,
        IReadOnlyList<Guid> callerUnitChain,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ConfigurationSnapshot snapshot = await LoadAsync(cancellationToken);

        // An undeclared flag is off. Defaulting to on would make every
        // misspelled key a silent release.
        return snapshot.Flags.TryGetValue(key.Trim().ToLowerInvariant(), out FeatureFlag? flag)
               && flag.IsOnFor(callerRoleIds, callerUnitChain);
    }

    private async Task<ConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        long current = await versionStore.GetCurrentAsync(cancellationToken);

        if (cache.TryGet(out ConfigurationSnapshot? cached)
            && cached is not null
            && cached.Version == current)
        {
            return cached;
        }

        ConfigurationSnapshot loaded = await repository.LoadSnapshotAsync(cancellationToken);

        cache.Set(loaded);

        return loaded;
    }
}
