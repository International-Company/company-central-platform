namespace CCP.Modules.Configuration.Contracts.Dtos;

/// <summary>
/// A declared setting, and what has been set for it.
/// <para>
/// <b>A sensitive setting's value is absent from this shape</b> rather than
/// masked by the caller. There is no code path that could return it by
/// forgetting to check, because there is nothing to return.
/// </para>
/// </summary>
public sealed record SettingDto(
    string Key,
    string ApplicationCode,
    string ValueType,
    string? Description,
    string? DefaultValue,
    bool IsSensitive,
    long? Minimum,
    long? Maximum,
    IReadOnlyList<string> AllowedValues,
    IReadOnlyList<SettingValueDto> Values);

/// <summary>One override, at one scope.</summary>
public sealed record SettingValueDto(
    string Scope,
    Guid? ScopeId,
    string Value,
    DateTimeOffset SetAt);

/// <summary>
/// One change: what it was, what it became, who and when.
/// <para>
/// For a sensitive setting both values read as <c>[sensitive]</c>. A value that
/// cannot be read back through the API but sits in plain sight in its own change
/// log has not been protected — it has been moved.
/// </para>
/// </summary>
public sealed record SettingChangeDto(
    string Key,
    string Scope,
    Guid? ScopeId,
    string? OldValue,
    string? NewValue,
    Guid ChangedBy,
    DateTimeOffset ChangedAt,
    string? Reason);

/// <summary>A capability that can be switched off without a deployment.</summary>
public sealed record FeatureFlagDto(
    string Key,
    string ApplicationCode,
    string? Description,
    bool IsEnabled,
    bool IsUntargeted,
    IReadOnlyList<Guid> TargetedRoles,
    IReadOnlyList<Guid> TargetedUnits,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Whether a feature is on for the caller asking.
/// <para>
/// The answer, and nothing about why. A caller learning which roles a flag
/// targets would be learning the shape of a rollout it is not part of.
/// </para>
/// </summary>
public sealed record FeatureStateDto(string Key, bool IsOn);
