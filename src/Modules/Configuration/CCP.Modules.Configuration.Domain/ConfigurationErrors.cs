using CCP.Kernel.Results;

namespace CCP.Modules.Configuration.Domain;

/// <summary>
/// Every way declaring or changing a setting can be refused.
/// </summary>
public static class ConfigurationErrors
{
    // --- Definitions --------------------------------------------------------

    public static readonly Error KeyRequired = Error.Validation(
        "CONFIG.KEY_REQUIRED", "A setting needs a key.", "key");

    public static readonly Error ApplicationCodeRequired = Error.Validation(
        "CONFIG.APPLICATION_CODE_REQUIRED", "A setting needs an owning application.", "applicationCode");

    public static readonly Error KeyShape = Error.Validation(
        "CONFIG.KEY_SHAPE",
        "A key looks like application.area.setting — three parts at least.",
        "key");

    public static readonly Error KeyCharacters = Error.Validation(
        "CONFIG.KEY_CHARACTERS",
        "A key may contain letters, digits, dots and hyphens.",
        "key");

    /// <summary>
    /// A setting declared outside its owner's namespace.
    /// <para>
    /// The same rule as permissions, for the same reason: one system must not be
    /// able to redefine another's behaviour by declaring a setting in its name.
    /// </para>
    /// </summary>
    public static Error KeyOutsideNamespace(string applicationCode) => Error.Rule(
        "CONFIG.KEY_OUTSIDE_NAMESPACE",
        $"A setting owned by '{applicationCode}' must begin with '{applicationCode}.'.");

    public static readonly Error DefinitionNotFound = Error.NotFound(
        "CONFIG.DEFINITION_NOT_FOUND", "No setting is declared with that key.");

    public static readonly Error KeyTaken = Error.Conflict(
        "CONFIG.KEY_TAKEN", "A setting with that key is already declared.");

    public static readonly Error RangeInverted = Error.Validation(
        "CONFIG.RANGE_INVERTED", "The minimum is greater than the maximum.", "minimum");

    public static readonly Error DefaultViolatesConstraints = Error.Conflict(
        "CONFIG.DEFAULT_VIOLATES_CONSTRAINTS",
        "The setting's own default would no longer be valid under these constraints.");

    // --- Values -------------------------------------------------------------

    public static readonly Error NotABoolean = Error.Validation(
        "CONFIG.NOT_A_BOOLEAN", "This setting takes true or false.", "value");

    public static readonly Error NotAnInteger = Error.Validation(
        "CONFIG.NOT_AN_INTEGER", "This setting takes a whole number.", "value");

    public static readonly Error NotADuration = Error.Validation(
        "CONFIG.NOT_A_DURATION",
        "This setting takes a duration, such as 00:15:00.",
        "value");

    public static readonly Error NotAUrl = Error.Validation(
        "CONFIG.NOT_A_URL", "This setting takes an absolute http or https URL.", "value");

    public static Error BelowMinimum(long minimum) => Error.Validation(
        "CONFIG.BELOW_MINIMUM", $"The smallest permitted value is {minimum}.", "value");

    public static Error AboveMaximum(long maximum) => Error.Validation(
        "CONFIG.ABOVE_MAXIMUM", $"The largest permitted value is {maximum}.", "value");

    public static Error NotAnAllowedValue(IReadOnlyList<string> choices) => Error.Validation(
        "CONFIG.NOT_AN_ALLOWED_VALUE",
        $"This setting takes one of: {string.Join(", ", choices)}.",
        "value");

    /// <summary>
    /// Somebody put a secret in a settings table.
    /// <para>
    /// Refused whatever the sensitivity flag says. Sensitivity stops a value
    /// being read back; it does not stop it being in the database, in every
    /// backup of it, and in the hands of whoever gets a copy. Secrets live in
    /// the secret manager and are named by reference (§21.1).
    /// </para>
    /// </summary>
    public static readonly Error SecretShapedValue = Error.Validation(
        "CONFIG.SECRET_SHAPED_VALUE",
        "That looks like a secret. Settings are stored in plaintext and appear in backups and "
        + "exports, so a secret belongs in the secret store and is named here by reference.",
        "value");

    public static readonly Error ScopeIdRequired = Error.Validation(
        "CONFIG.SCOPE_ID_REQUIRED",
        "A company or application setting needs to say which one.",
        "scopeId");

    public static readonly Error ScopeIdNotApplicable = Error.Validation(
        "CONFIG.SCOPE_ID_NOT_APPLICABLE",
        "A Platform-wide setting is not qualified by anything.",
        "scopeId");

    public static readonly Error ValueNotFound = Error.NotFound(
        "CONFIG.VALUE_NOT_FOUND", "Nothing has been set at that scope.");

    /// <summary>
    /// An attempt to read a value that may be written and not read.
    /// </summary>
    public static readonly Error SensitiveValueNotReadable = Error.Forbidden(
        "CONFIG.SENSITIVE_VALUE_NOT_READABLE",
        "This setting is marked sensitive. It can be changed and not read back.");

    // --- Feature flags ------------------------------------------------------

    public static readonly Error FlagNotFound = Error.NotFound(
        "CONFIG.FLAG_NOT_FOUND", "No feature flag is declared with that key.");

    public static readonly Error FlagKeyTaken = Error.Conflict(
        "CONFIG.FLAG_KEY_TAKEN", "A feature flag with that key is already declared.");
}
