using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Configuration.Application.Abstractions;
using CCP.Modules.Configuration.Contracts.Dtos;
using CCP.Modules.Configuration.Domain;
using CCP.Modules.Configuration.Domain.Flags;
using CCP.Modules.Configuration.Domain.Settings;

namespace CCP.Modules.Configuration.Application;

/// <summary>The module name every audit entry here carries.</summary>
internal static class ConfigurationAudit
{
    public const string ModuleName = "configuration";
}

// ---------------------------------------------------------------------------
// Commands and queries
// ---------------------------------------------------------------------------

/// <summary>Declaring a setting an application owns.</summary>
public sealed record DeclareSettingCommand(
    string Key,
    string ApplicationCode,
    string ValueType,
    string? Description,
    string? DefaultValue,
    bool IsSensitive,
    long? Minimum,
    long? Maximum,
    IReadOnlyList<string>? AllowedValues);

/// <summary>Setting a value at a scope, or clearing it.</summary>
public sealed record SetSettingCommand(
    string Key,
    string Scope,
    Guid? ScopeId,
    string? Value,
    string? Reason,
    Guid ActingUserId);

public sealed record DeclareFlagCommand(string Key, string ApplicationCode, string? Description);

public sealed record SetFlagCommand(
    string Key,
    bool IsEnabled,
    IReadOnlyList<Guid>? RoleIds,
    IReadOnlyList<Guid>? UnitIds,
    Guid ActingUserId);

// ---------------------------------------------------------------------------
// Mapping
// ---------------------------------------------------------------------------

/// <summary>
/// Turns the module's types into the shapes it publishes.
/// <para>
/// <b>A sensitive setting's value never appears in a DTO.</b> Not masked in the
/// screen, not filtered by the caller — absent from the shape, so there is no
/// code path that could return it by forgetting to check.
/// </para>
/// </summary>
public static class ConfigurationMapper
{
    /// <summary>What a sensitive value reads as. Recognisable, and not a value.</summary>
    public const string Hidden = "[sensitive]";

    public static SettingDto ToDto(
        SettingDefinition definition, IReadOnlyList<SettingValue> values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        return new SettingDto(
            definition.Key,
            definition.ApplicationCode,
            definition.ValueType.ToString(),
            definition.Description,
            definition.IsSensitive ? null : definition.DefaultValue,
            definition.IsSensitive,
            definition.Minimum,
            definition.Maximum,
            definition.Choices,
            [.. values.Select(v => ToDto(v, definition.IsSensitive))]);
    }

    public static SettingValueDto ToDto(SettingValue value, bool isSensitive)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new SettingValueDto(
            value.Scope.ToString(),
            value.ScopeId,
            isSensitive ? Hidden : value.Value,
            value.UpdatedAt ?? value.CreatedAt);
    }

    public static SettingChangeDto ToDto(SettingChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new SettingChangeDto(
            change.Key,
            change.Scope.ToString(),
            change.ScopeId,
            change.OldValue,
            change.NewValue,
            change.ChangedBy,
            change.ChangedAt,
            change.Reason);
    }

    public static FeatureFlagDto ToDto(FeatureFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        return new FeatureFlagDto(
            flag.Key,
            flag.ApplicationCode,
            flag.Description,
            flag.IsEnabled,
            flag.IsUntargeted,
            flag.RoleTargets,
            flag.UnitTargets,
            flag.UpdatedAt ?? flag.CreatedAt);
    }
}

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

/// <summary>
/// Declares a setting.
/// <para>
/// Idempotent on the key: declaring one that exists updates its description and
/// constraints rather than failing. An application sends its declarations on
/// every startup, and a second startup must not be an error.
/// </para>
/// </summary>
public sealed class DeclareSettingHandler(
    IConfigurationRepository repository,
    IConfigurationVersionStore versionStore,
    IConfigurationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<SettingDto>> HandleAsync(
        DeclareSettingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Enum.TryParse(command.ValueType, ignoreCase: true, out SettingValueType type))
        {
            return Result.Failure<SettingDto>(ConfigurationErrors.KeyShape);
        }

        DateTimeOffset now = clock.UtcNow;

        SettingDefinition? existing =
            await repository.FindDefinitionAsync(command.Key, cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(
                    existing.ApplicationCode,
                    command.ApplicationCode.Trim().ToLowerInvariant(),
                    StringComparison.Ordinal))
            {
                // Somebody else's setting. Refused rather than adopted: a
                // declaration must not be a way to take ownership of a key.
                return Result.Failure<SettingDto>(
                    ConfigurationErrors.KeyOutsideNamespace(existing.ApplicationCode));
            }

            Result updated = Apply(existing, command, now);

            if (updated.IsFailure)
            {
                return Result.Failure<SettingDto>(updated.Errors);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await versionStore.BumpAsync(cancellationToken);

            return Result.Success(ConfigurationMapper.ToDto(existing, []));
        }

        Result<SettingDefinition> declared = SettingDefinition.Declare(
            command.Key, command.ApplicationCode, type, command.Description, null, now);

        if (declared.IsFailure)
        {
            return Result.Failure<SettingDto>(declared.Errors);
        }

        Result applied = Apply(declared.Value, command, now);

        if (applied.IsFailure)
        {
            return Result.Failure<SettingDto>(applied.Errors);
        }

        repository.AddDefinition(declared.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await versionStore.BumpAsync(cancellationToken);

        return Result.Success(ConfigurationMapper.ToDto(declared.Value, []));
    }

    /// <summary>
    /// Constraints before the default, because the default is validated against
    /// them.
    /// </summary>
    private static Result Apply(
        SettingDefinition definition, DeclareSettingCommand command, DateTimeOffset now)
    {
        definition.MarkSensitive(command.IsSensitive, now);

        Result constrained = definition.SetConstraints(
            command.Minimum, command.Maximum, command.AllowedValues, now);

        return constrained.IsFailure
            ? constrained
            : definition.SetDefault(command.DefaultValue, now);
    }
}

/// <summary>
/// Sets a value at a scope, or removes the override.
/// <para>
/// <b>Every change is recorded with what it was and what it became</b>, because
/// "what is it now" is never the question being asked when a behaviour changed
/// and nobody remembers doing it.
/// </para>
/// </summary>
public sealed class SetSettingHandler(
    IConfigurationRepository repository,
    IConfigurationVersionStore versionStore,
    IConfigurationCache cache,
    IAuditTrail auditTrail,
    IConfigurationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        SetSettingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Enum.TryParse(command.Scope, ignoreCase: true, out SettingScope scope))
        {
            return Result.Failure(ConfigurationErrors.ScopeIdRequired);
        }

        SettingDefinition? definition =
            await repository.FindDefinitionAsync(command.Key, cancellationToken);

        if (definition is null)
        {
            return Result.Failure(ConfigurationErrors.DefinitionNotFound);
        }

        DateTimeOffset now = clock.UtcNow;

        SettingValue? existing = await repository.FindValueAsync(
            definition.Key, scope, command.ScopeId, cancellationToken);

        string? oldValue = existing?.Value;

        if (command.Value is null)
        {
            // Clearing the override, which is a different act from setting it to
            // an empty string: the setting falls back to the scope above.
            if (existing is null)
            {
                return Result.Failure(ConfigurationErrors.ValueNotFound);
            }

            repository.RemoveValue(existing);
        }
        else if (existing is not null)
        {
            Result<string> changed = existing.Change(definition, command.Value, now);

            if (changed.IsFailure)
            {
                return Result.Failure(changed.Errors);
            }
        }
        else
        {
            Result<SettingValue> created = SettingValue.Set(
                definition, scope, command.ScopeId, command.Value, now);

            if (created.IsFailure)
            {
                return Result.Failure(created.Errors);
            }

            repository.AddValue(created.Value);
        }

        repository.AddChange(SettingChange.Record(
            definition, scope, command.ScopeId, oldValue, command.Value,
            command.ActingUserId, now, command.Reason));

        await auditTrail.RecordAsync(
            new AuditEntry(
                ConfigurationAudit.ModuleName,
                "setting.changed",
                AuditOutcome.Success,
                "setting",
                definition.Key,
                // The values go to the change history, which hides them for a
                // sensitive setting. The audit trail records that it happened.
                NewValue: $$"""{"scope":"{{scope}}","sensitive":{{definition.IsSensitive.ToString().ToLowerInvariant()}}}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Both, and in this order. The stamp is what makes the change visible to
        // every instance; clearing the local copy just saves this one a reload.
        await versionStore.BumpAsync(cancellationToken);
        cache.Clear();

        return Result.Success();
    }
}

/// <summary>Declares a feature flag. Off until somebody turns it on.</summary>
public sealed class DeclareFlagHandler(
    IConfigurationRepository repository,
    IConfigurationVersionStore versionStore,
    IConfigurationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<FeatureFlagDto>> HandleAsync(
        DeclareFlagCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        FeatureFlag? existing = await repository.FindFlagAsync(command.Key, cancellationToken);

        if (existing is not null)
        {
            // Declaring again is not an error and does not disturb the switch.
            // An application declares its flags on every startup, and a restart
            // must not turn something on or off.
            return Result.Success(ConfigurationMapper.ToDto(existing));
        }

        Result<FeatureFlag> declared = FeatureFlag.Declare(
            command.Key, command.ApplicationCode, command.Description, clock.UtcNow);

        if (declared.IsFailure)
        {
            return Result.Failure<FeatureFlagDto>(declared.Errors);
        }

        repository.AddFlag(declared.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await versionStore.BumpAsync(cancellationToken);

        return Result.Success(ConfigurationMapper.ToDto(declared.Value));
    }
}

/// <summary>Turns a flag on or off, and decides who it reaches.</summary>
public sealed class SetFlagHandler(
    IConfigurationRepository repository,
    IConfigurationVersionStore versionStore,
    IConfigurationCache cache,
    IAuditTrail auditTrail,
    IConfigurationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<FeatureFlagDto>> HandleAsync(
        SetFlagCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        FeatureFlag? flag = await repository.FindFlagAsync(command.Key, cancellationToken);

        if (flag is null)
        {
            return Result.Failure<FeatureFlagDto>(ConfigurationErrors.FlagNotFound);
        }

        DateTimeOffset now = clock.UtcNow;
        bool wasEnabled = flag.IsEnabled;

        flag.SetEnabled(command.IsEnabled, now);
        flag.Target(command.RoleIds ?? [], command.UnitIds ?? [], now);

        await auditTrail.RecordAsync(
            new AuditEntry(
                ConfigurationAudit.ModuleName,
                command.IsEnabled ? "flag.enabled" : "flag.disabled",
                AuditOutcome.Success,
                "feature-flag",
                flag.Key,
                OldValue: $$"""{"enabled":{{wasEnabled.ToString().ToLowerInvariant()}}}""",
                NewValue: $$"""{"enabled":{{command.IsEnabled.ToString().ToLowerInvariant()}},"targeted":{{(!flag.IsUntargeted).ToString().ToLowerInvariant()}}}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await versionStore.BumpAsync(cancellationToken);
        cache.Clear();

        return Result.Success(ConfigurationMapper.ToDto(flag));
    }
}

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

/// <summary>Every declared setting, with what has been set.</summary>
public sealed class GetSettingsHandler(IConfigurationRepository repository)
{
    public async Task<Result<IReadOnlyList<SettingDto>>> HandleAsync(
        string? applicationCode, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SettingDefinition> definitions =
            await repository.GetDefinitionsAsync(applicationCode, cancellationToken);

        var dtos = new List<SettingDto>(definitions.Count);

        foreach (SettingDefinition definition in definitions)
        {
            IReadOnlyList<SettingValue> values =
                await repository.GetValuesAsync(definition.Key, cancellationToken);

            dtos.Add(ConfigurationMapper.ToDto(definition, values));
        }

        return Result.Success<IReadOnlyList<SettingDto>>(dtos);
    }
}

/// <summary>What one setting has been through.</summary>
public sealed class GetSettingHistoryHandler(IConfigurationRepository repository)
{
    public async Task<Result<IReadOnlyList<SettingChangeDto>>> HandleAsync(
        string key, CancellationToken cancellationToken = default)
    {
        SettingDefinition? definition =
            await repository.FindDefinitionAsync(key, cancellationToken);

        if (definition is null)
        {
            return Result.Failure<IReadOnlyList<SettingChangeDto>>(
                ConfigurationErrors.DefinitionNotFound);
        }

        IReadOnlyList<SettingChange> changes =
            await repository.GetChangesAsync(definition.Key, limit: 100, cancellationToken);

        return Result.Success<IReadOnlyList<SettingChangeDto>>(
            [.. changes.Select(ConfigurationMapper.ToDto)]);
    }
}

/// <summary>Every declared flag and its switch.</summary>
public sealed class GetFlagsHandler(IConfigurationRepository repository)
{
    public async Task<Result<IReadOnlyList<FeatureFlagDto>>> HandleAsync(
        string? applicationCode, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FeatureFlag> flags =
            await repository.GetFlagsAsync(applicationCode, cancellationToken);

        return Result.Success<IReadOnlyList<FeatureFlagDto>>(
            [.. flags.Select(ConfigurationMapper.ToDto)]);
    }
}
