using CCP.Kernel.Security;
using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Configuration.Domain.Settings;

/// <summary>
/// What a setting is: its name, its type, and the rules its values must obey.
/// <para>
/// <b>Settings are declared before they are set.</b> A store of free-form
/// key-value pairs looks simpler for about a week, and then nobody can say what
/// keys exist, what a value means, or whether a typo created a new setting or
/// broke an old one. A definition is what makes a value checkable and a
/// misspelling an error.
/// </para>
/// <para>
/// <b>What does not belong here.</b> Configuration governs how the Platform
/// behaves — how long a session lasts, how large a document may be, whether a
/// capability is switched on. It does not hold business parameters: a tax rate,
/// a fee, an approval threshold. Those belong to the business system that
/// understands them, and putting one here would make the Platform hold a rule it
/// cannot reason about, which is the boundary this whole project is built to
/// keep.
/// </para>
/// </summary>
public sealed class SettingDefinition : AggregateRoot, IAuditableEntity
{
    private SettingDefinition() { }

    private SettingDefinition(
        Guid id,
        string key,
        string applicationCode,
        SettingValueType valueType,
        string? defaultValue,
        DateTimeOffset now)
        : base(id)
    {
        Key = key;
        ApplicationCode = applicationCode;
        ValueType = valueType;
        DefaultValue = defaultValue;
        CreatedAt = now;
    }

    /// <summary>
    /// The full name, in the same shape as a permission:
    /// <c>&lt;application&gt;.&lt;area&gt;.&lt;setting&gt;</c>.
    /// </summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>
    /// Who owns it. A setting must sit in its owner's namespace, which is what
    /// stops one system quietly redefining another's behaviour.
    /// </summary>
    public string ApplicationCode { get; private set; } = string.Empty;

    public SettingValueType ValueType { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// What the value is when nobody has set one. Already validated against the
    /// type, so an unset setting can never be an invalid one.
    /// </summary>
    public string? DefaultValue { get; private set; }

    /// <summary>
    /// Whether reading this back is refused.
    /// <para>
    /// <b>A sensitive setting can be written and never read</b> — not by an
    /// administrator, not through any API, not in the change history. It exists
    /// for values that are configuration rather than secrets but still should
    /// not be on a screen: an internal hostname, a support contact nobody should
    /// harvest.
    /// </para>
    /// <para>
    /// It is <i>not</i> a way to store secrets. Those live in the secret manager
    /// and are named by reference; a value that looks like one is refused
    /// outright, whatever this flag says.
    /// </para>
    /// </summary>
    public bool IsSensitive { get; private set; }

    /// <summary>
    /// For a number, the smallest permitted value; for text, the shortest.
    /// Null when unbounded.
    /// </summary>
    public long? Minimum { get; private set; }

    public long? Maximum { get; private set; }

    /// <summary>
    /// The permitted values, newline-separated, for a setting that is a choice.
    /// <para>
    /// A closed list is worth having because most settings that look like free
    /// text are not: a log level, a locale, a strategy name. Typing one of those
    /// wrongly should be refused when it is typed, not discovered by the code
    /// that reads it.
    /// </para>
    /// </summary>
    public string? AllowedValues { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>The permitted values, parsed.</summary>
    public IReadOnlyList<string> Choices =>
        string.IsNullOrEmpty(AllowedValues)
            ? []
            : AllowedValues.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Result<SettingDefinition> Declare(
        string key,
        string applicationCode,
        SettingValueType valueType,
        string? description,
        string? defaultValue,
        DateTimeOffset now)
    {
        Result<string> validKey = ValidateKey(key, applicationCode);

        if (validKey.IsFailure)
        {
            return Result.Failure<SettingDefinition>(validKey.Errors);
        }

        var definition = new SettingDefinition(
            Uuid7.NewGuid(now),
            validKey.Value,
            applicationCode.Trim().ToLowerInvariant(),
            valueType,
            null,
            now)
        {
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        };

        if (defaultValue is not null)
        {
            // The default goes through the same validation as any other value.
            // A default that could not be set by hand is a setting that is
            // invalid until somebody changes it, which nobody would notice.
            Result applied = definition.SetDefault(defaultValue, now);

            if (applied.IsFailure)
            {
                return Result.Failure<SettingDefinition>(applied.Errors);
            }
        }

        return Result.Success(definition);
    }

    public Result SetDefault(string? defaultValue, DateTimeOffset now)
    {
        if (defaultValue is null)
        {
            DefaultValue = null;
            UpdatedAt = now;

            return Result.Success();
        }

        Result valid = Validate(defaultValue);

        if (valid.IsFailure)
        {
            return valid;
        }

        DefaultValue = defaultValue;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result SetConstraints(
        long? minimum, long? maximum, IReadOnlyList<string>? allowedValues, DateTimeOffset now)
    {
        if (minimum is { } low && maximum is { } high && low > high)
        {
            return Result.Failure(ConfigurationErrors.RangeInverted);
        }

        Minimum = minimum;
        Maximum = maximum;

        AllowedValues = allowedValues is { Count: > 0 }
            ? string.Join('\n', allowedValues.Select(v => v.Trim()).Where(v => v.Length > 0))
            : null;

        UpdatedAt = now;

        // A default that was valid under the old constraints may not be under
        // the new ones. Refused rather than left inconsistent, because a
        // definition whose own default it rejects is a trap for whoever reads
        // it next.
        if (DefaultValue is not null && Validate(DefaultValue).IsFailure)
        {
            return Result.Failure(ConfigurationErrors.DefaultViolatesConstraints);
        }

        return Result.Success();
    }

    public Result MarkSensitive(bool isSensitive, DateTimeOffset now)
    {
        IsSensitive = isSensitive;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Whether a value is acceptable for this setting.
    /// <para>
    /// Type first, then range, then the closed list. Everything here is checked
    /// at the moment somebody types it, which is the only moment at which a
    /// helpful message is possible — the alternative is a parse failure inside
    /// whatever reads the setting, three weeks later, with no clue who set it.
    /// </para>
    /// </summary>
    public Result Validate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Before anything else, and regardless of every other rule. A secret in
        // a settings table is a secret in every backup, every export and every
        // administration screen — and the sensitivity flag does not change that,
        // because the value is still stored in plaintext beside everything else.
        if (SecretShapedValue.Looks(value))
        {
            return Result.Failure(ConfigurationErrors.SecretShapedValue);
        }

        Result typed = ValidateType(value);

        if (typed.IsFailure)
        {
            return typed;
        }

        Result bounded = ValidateBounds(value);

        if (bounded.IsFailure)
        {
            return bounded;
        }

        IReadOnlyList<string> choices = Choices;

        return choices.Count > 0 && !choices.Contains(value, StringComparer.Ordinal)
            ? Result.Failure(ConfigurationErrors.NotAnAllowedValue(choices))
            : Result.Success();
    }

    private Result ValidateType(string value) => ValueType switch
    {
        SettingValueType.Boolean => bool.TryParse(value, out _)
            ? Result.Success()
            : Result.Failure(ConfigurationErrors.NotABoolean),

        SettingValueType.Number => long.TryParse(
            value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out _)
            ? Result.Success()
            : Result.Failure(ConfigurationErrors.NotAnInteger),

        SettingValueType.Duration => TimeSpan.TryParse(
            value, System.Globalization.CultureInfo.InvariantCulture, out _)
            ? Result.Success()
            : Result.Failure(ConfigurationErrors.NotADuration),

        SettingValueType.Url => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? Result.Success()
            : Result.Failure(ConfigurationErrors.NotAUrl),

        _ => Result.Success()
    };

    /// <summary>
    /// Range, meaning the number for a number and the length for text.
    /// <para>
    /// One pair of columns for both, because "between 1 and 100" and "between 1
    /// and 100 characters" are the same shape of rule and two pairs would be two
    /// places to check.
    /// </para>
    /// </summary>
    private Result ValidateBounds(string value)
    {
        if (Minimum is null && Maximum is null)
        {
            return Result.Success();
        }

        long measured = ValueType == SettingValueType.Number
            ? long.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : value.Length;

        if (Minimum is { } low && measured < low)
        {
            return Result.Failure(ConfigurationErrors.BelowMinimum(low));
        }

        return Maximum is { } high && measured > high
            ? Result.Failure(ConfigurationErrors.AboveMaximum(high))
            : Result.Success();
    }

    /// <summary>
    /// <c>application.area.setting</c>, in its owner's namespace.
    /// </summary>
    private static Result<string> ValidateKey(string key, string applicationCode)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<string>(ConfigurationErrors.KeyRequired);
        }

        if (string.IsNullOrWhiteSpace(applicationCode))
        {
            return Result.Failure<string>(ConfigurationErrors.ApplicationCodeRequired);
        }

        string trimmed = key.Trim().ToLowerInvariant();
        string prefix = applicationCode.Trim().ToLowerInvariant() + ".";

        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            // The same rule as permissions, for the same reason: one system
            // must not be able to redefine another's behaviour by declaring a
            // setting in its namespace.
            return Result.Failure<string>(
                ConfigurationErrors.KeyOutsideNamespace(applicationCode));
        }

        if (trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries).Length < 3)
        {
            return Result.Failure<string>(ConfigurationErrors.KeyShape);
        }

        return trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
            ? Result.Success(trimmed)
            : Result.Failure<string>(ConfigurationErrors.KeyCharacters);
    }
}

/// <summary>What kind of value a setting holds.</summary>
public enum SettingValueType
{
    /// <summary>Free text, bounded by length.</summary>
    Text = 1,

    /// <summary>
    /// A whole number, bounded by value.
    /// <para>
    /// Named <c>Number</c> rather than <c>Integer</c> because an analyser
    /// objects to a member named after a type, and it is right to: a reader
    /// seeing <c>Integer</c> in a switch has to work out whether it means the
    /// CLR type or this setting kind.
    /// </para>
    /// </summary>
    Number = 2,

    /// <summary>True or false.</summary>
    Boolean = 3,

    /// <summary>A <c>TimeSpan</c>, such as <c>00:15:00</c>.</summary>
    Duration = 4,

    /// <summary>An absolute http or https URL.</summary>
    Url = 5
}
