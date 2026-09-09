using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Configuration.Domain.Settings;

/// <summary>
/// A value somebody has set, at a scope.
/// <para>
/// <b>A row exists only where somebody has overridden something.</b> The
/// definition carries the default, so a setting nobody has touched has no row at
/// all — which means adding a setting costs nothing, and a company that has
/// customised three things has three rows rather than four hundred.
/// </para>
/// </summary>
public sealed class SettingValue : AggregateRoot, IAuditableEntity
{
    private SettingValue() { }

    private SettingValue(
        Guid id,
        string key,
        SettingScope scope,
        Guid? scopeId,
        string value,
        DateTimeOffset now)
        : base(id)
    {
        Key = key;
        Scope = scope;
        ScopeId = scopeId;
        Value = value;
        CreatedAt = now;
    }

    public string Key { get; private set; } = string.Empty;

    public SettingScope Scope { get; private set; }

    /// <summary>
    /// Which company or application this value belongs to. Null at Platform
    /// scope, where there is nothing to qualify.
    /// </summary>
    public Guid? ScopeId { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<SettingValue> Set(
        SettingDefinition definition,
        SettingScope scope,
        Guid? scopeId,
        string value,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Result valid = definition.Validate(value);

        if (valid.IsFailure)
        {
            return Result.Failure<SettingValue>(valid.Errors);
        }

        Result scoped = ValidateScope(scope, scopeId);

        if (scoped.IsFailure)
        {
            return Result.Failure<SettingValue>(scoped.Errors);
        }

        return Result.Success(new SettingValue(
            Uuid7.NewGuid(now), definition.Key, scope, scopeId, value, now));
    }

    /// <summary>
    /// Changes the value, returning what it was.
    /// <para>
    /// The old value is handed back rather than discarded because the change
    /// history needs it, and reading it from the database a second time would be
    /// reading it after it had already been overwritten.
    /// </para>
    /// </summary>
    public Result<string> Change(SettingDefinition definition, string value, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Result valid = definition.Validate(value);

        if (valid.IsFailure)
        {
            return Result.Failure<string>(valid.Errors);
        }

        string previous = Value;

        Value = value;
        UpdatedAt = now;

        return Result.Success(previous);
    }

    private static Result ValidateScope(SettingScope scope, Guid? scopeId)
        => scope switch
        {
            SettingScope.Platform when scopeId is not null =>
                Result.Failure(ConfigurationErrors.ScopeIdNotApplicable),

            SettingScope.Company or SettingScope.Application when scopeId is null =>
                Result.Failure(ConfigurationErrors.ScopeIdRequired),

            _ => Result.Success()
        };
}

/// <summary>
/// How far a value reaches, narrowest last.
/// <para>
/// The order is the precedence: a value set for an application beats one set for
/// a company, which beats the Platform default. <b>Narrowest wins</b>, which is
/// the only rule anybody can hold in their head — the alternative, where a
/// broader setting can override a narrower one, produces the situation where
/// changing something at the top silently undoes somebody's deliberate local
/// decision.
/// </para>
/// </summary>
public enum SettingScope
{
    /// <summary>The whole Platform. The broadest, and the fallback.</summary>
    Platform = 1,

    /// <summary>One company, when the Platform serves more than one.</summary>
    Company = 2,

    /// <summary>One registered application. The narrowest.</summary>
    Application = 3
}

/// <summary>
/// One change to one setting, kept.
/// <para>
/// <b>Old value, new value, who and when</b> — the four things somebody needs
/// when a behaviour changed and nobody remembers doing it. A settings table
/// without this answers "what is it now" and nothing else, and "what is it now"
/// is never the question being asked during an incident.
/// </para>
/// <para>
/// Append-only. The value of a change history that can be edited is zero.
/// </para>
/// </summary>
public sealed class SettingChange : Entity
{
    private SettingChange() { }

    private SettingChange(
        Guid id,
        string key,
        SettingScope scope,
        Guid? scopeId,
        string? oldValue,
        string? newValue,
        Guid changedBy,
        DateTimeOffset changedAt,
        string? reason)
        : base(id)
    {
        Key = key;
        Scope = scope;
        ScopeId = scopeId;
        OldValue = oldValue;
        NewValue = newValue;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
        Reason = reason;
    }

    public string Key { get; private set; } = string.Empty;

    public SettingScope Scope { get; private set; }

    public Guid? ScopeId { get; private set; }

    /// <summary>
    /// What it was. Null when the setting was previously unset — which is a
    /// different fact from "it was empty", and the two are worth telling apart.
    /// </summary>
    public string? OldValue { get; private set; }

    /// <summary>What it became. Null when the override was removed.</summary>
    public string? NewValue { get; private set; }

    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>
    /// Why, in the changer's words. Optional, and worth asking for: six months
    /// later the value explains what, and only this explains why.
    /// </summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// Records a change, hiding the values when the setting is sensitive.
    /// <para>
    /// <b>The history of a sensitive setting says that it changed and not what
    /// to.</b> A value that cannot be read back through the API but sits in
    /// plain sight in its own change log has not been protected — it has been
    /// moved.
    /// </para>
    /// </summary>
    public static SettingChange Record(
        SettingDefinition definition,
        SettingScope scope,
        Guid? scopeId,
        string? oldValue,
        string? newValue,
        Guid changedBy,
        DateTimeOffset now,
        string? reason)
    {
        ArgumentNullException.ThrowIfNull(definition);

        const string Hidden = "[sensitive]";

        return new SettingChange(
            Uuid7.NewGuid(now),
            definition.Key,
            scope,
            scopeId,
            definition.IsSensitive && oldValue is not null ? Hidden : oldValue,
            definition.IsSensitive && newValue is not null ? Hidden : newValue,
            changedBy,
            now,
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
    }
}
