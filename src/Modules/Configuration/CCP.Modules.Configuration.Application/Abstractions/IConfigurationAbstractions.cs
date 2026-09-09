using CCP.Kernel.Application.Abstractions;
using CCP.Modules.Configuration.Domain.Flags;
using CCP.Modules.Configuration.Domain.Settings;

namespace CCP.Modules.Configuration.Application.Abstractions;

/// <summary>Commits the module's changes as one transaction.</summary>
public interface IConfigurationUnitOfWork : IUnitOfWork;

/// <summary>Reading and writing the module's own tables.</summary>
public interface IConfigurationRepository
{
    Task<SettingDefinition?> FindDefinitionAsync(
        string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingDefinition>> GetDefinitionsAsync(
        string? applicationCode, CancellationToken cancellationToken = default);

    void AddDefinition(SettingDefinition definition);

    /// <summary>
    /// Every value set for a key, at every scope.
    /// <para>
    /// All of them at once rather than one lookup per scope, because resolution
    /// walks from narrowest to broadest and would otherwise be three queries to
    /// answer one question.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SettingValue>> GetValuesAsync(
        string key, CancellationToken cancellationToken = default);

    Task<SettingValue?> FindValueAsync(
        string key, SettingScope scope, Guid? scopeId, CancellationToken cancellationToken = default);

    void AddValue(SettingValue value);

    void RemoveValue(SettingValue value);

    void AddChange(SettingChange change);

    Task<IReadOnlyList<SettingChange>> GetChangesAsync(
        string key, int limit, CancellationToken cancellationToken = default);

    // --- Feature flags ------------------------------------------------------

    Task<FeatureFlag?> FindFlagAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureFlag>> GetFlagsAsync(
        string? applicationCode, CancellationToken cancellationToken = default);

    void AddFlag(FeatureFlag flag);

    /// <summary>
    /// The whole configuration, for the cache to load in one pass.
    /// <para>
    /// Everything, because the alternative is a query per key on a path that
    /// runs on every request that reads a setting. The whole table is small by
    /// construction: definitions are declared by developers and values exist
    /// only where somebody has overridden something.
    /// </para>
    /// </summary>
    Task<ConfigurationSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything the resolver needs, read once.
/// </summary>
/// <param name="Definitions">Every declared setting, by key.</param>
/// <param name="Values">Every override, grouped by key.</param>
/// <param name="Flags">Every declared flag, by key.</param>
/// <param name="Version">
/// The stamp this was read at. A snapshot whose stamp no longer matches the
/// current one is stale and is discarded rather than used — the same mechanism
/// the permission cache uses, and for the same reason: a change to a setting
/// must take effect on the very next request, and a time-to-live leaves a window
/// nobody can reason about during an incident.
/// </param>
public sealed record ConfigurationSnapshot(
    IReadOnlyDictionary<string, SettingDefinition> Definitions,
    IReadOnlyDictionary<string, IReadOnlyList<SettingValue>> Values,
    IReadOnlyDictionary<string, FeatureFlag> Flags,
    long Version);

/// <summary>
/// The stamp that says whether a cached snapshot is still good.
/// <para>
/// Held in the database rather than in memory, so an instance that did not make
/// the change still notices it. Without that, a Platform running three instances
/// would apply a setting change on one of them.
/// </para>
/// </summary>
public interface IConfigurationVersionStore
{
    Task<long> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task BumpAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads settings and evaluates flags.
/// <para>
/// The one entry point everything else uses, and therefore the one place caching
/// happens.
/// </para>
/// </summary>
public interface IConfigurationReader
{
    /// <summary>
    /// A setting's value at a scope, falling back through the scopes above it.
    /// <para>
    /// Returns the definition's default when nothing has been set anywhere, and
    /// null only when the key was never declared — a distinction worth keeping,
    /// because one is a working setting and the other is a typo.
    /// </para>
    /// </summary>
    Task<string?> GetAsync(
        string key,
        Guid? companyId = null,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default);

    Task<bool> GetBooleanAsync(
        string key, bool fallback = false, CancellationToken cancellationToken = default);

    Task<int> GetIntegerAsync(
        string key, int fallback = 0, CancellationToken cancellationToken = default);

    Task<TimeSpan> GetDurationAsync(
        string key, TimeSpan fallback = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a feature is on for this caller.
    /// <para>
    /// An undeclared flag is <b>off</b>. A typo in a flag key must not switch a
    /// capability on, and defaulting to on would make every misspelling a
    /// silent release.
    /// </para>
    /// </summary>
    Task<bool> IsFeatureOnAsync(
        string key,
        IReadOnlyList<Guid> callerRoleIds,
        IReadOnlyList<Guid> callerUnitChain,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Holds the loaded configuration between requests.
/// </summary>
public interface IConfigurationCache
{
    bool TryGet(out ConfigurationSnapshot? snapshot);

    void Set(ConfigurationSnapshot snapshot);

    void Clear();
}
