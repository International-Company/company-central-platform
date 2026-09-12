using CCP.Kernel.Results;

namespace CCP.Modules.Notifications.Domain;

/// <summary>
/// Every way sending can be refused, said in words a person can act on.
/// </summary>
public static class NotificationErrors
{
    // --- Templates ----------------------------------------------------------

    public static readonly Error TemplateCodeRequired = Error.Validation(
        "NOTIFICATIONS.TEMPLATE_CODE_REQUIRED", "A template needs a code.", "code");

    public static readonly Error TemplateBodyRequired = Error.Validation(
        "NOTIFICATIONS.TEMPLATE_BODY_REQUIRED", "A template needs a body.", "body");

    public static readonly Error TemplateAlreadyInThatState = Error.Conflict(
        "NOTIFICATIONS.TEMPLATE_ALREADY_IN_STATE", "The template is already in that state.");

    public static Error UnsupportedLocale(string locale) => Error.Validation(
        "NOTIFICATIONS.UNSUPPORTED_LOCALE",
        $"'{locale}' is not a language this Platform speaks. Templates exist in Arabic and English.",
        "locale");

    /// <summary>
    /// The body refers to something the template does not declare.
    /// <para>
    /// Caught at authoring time, because the alternative is a message already
    /// sent with a gap where a number should be.
    /// </para>
    /// </summary>
    public static Error UndeclaredVariables(IReadOnlyList<string> names) => Error.Validation(
        "NOTIFICATIONS.UNDECLARED_VARIABLES",
        $"The text uses {string.Join(", ", names)}, which the template does not declare.",
        "variables");

    public static Error MissingVariables(IReadOnlyList<string> names) => Error.Validation(
        "NOTIFICATIONS.MISSING_VARIABLES",
        $"This template needs {string.Join(", ", names)}, and they were not supplied.",
        "variables");

    /// <summary>
    /// A locale has no template for this message.
    /// <para>
    /// Refused rather than substituted. A director receiving an approval request
    /// in the wrong language is a failure the company sees, and a silent English
    /// fallback is how that ships.
    /// </para>
    /// </summary>
    public static Error NoTemplateForLocale(string code, string locale) => Error.NotFound(
        "NOTIFICATIONS.NO_TEMPLATE_FOR_LOCALE",
        $"There is no '{code}' template in '{locale}'. Every template must exist in both languages.");

    // --- Notifications ------------------------------------------------------

    public static readonly Error RecipientRequired = Error.Validation(
        "NOTIFICATIONS.RECIPIENT_REQUIRED", "A notification needs a recipient.", "recipientUserId");

    public static readonly Error CategoryRequired = Error.Validation(
        "NOTIFICATIONS.CATEGORY_REQUIRED", "A notification needs a category.", "category");

    public static readonly Error BodyRequired = Error.Validation(
        "NOTIFICATIONS.BODY_REQUIRED", "A notification needs something to say.", "body");

    public static readonly Error NotificationNotFound = Error.NotFound(
        "NOTIFICATIONS.NOT_FOUND", "The notification does not exist.");

    public static readonly Error NotTheRecipient = Error.Forbidden(
        "NOTIFICATIONS.NOT_THE_RECIPIENT", "This notification is not yours.");

    public static readonly Error AlreadyDelivered = Error.Conflict(
        "NOTIFICATIONS.ALREADY_DELIVERED", "This notification has already been delivered.");

    // --- Channels and preferences -------------------------------------------

    public static readonly Error SecurityNotificationsCannotBeDisabled = Error.Rule(
        "NOTIFICATIONS.SECURITY_CANNOT_BE_DISABLED",
        "Security notifications cannot be turned off. They tell you your account may have "
        + "been taken, and the person most likely to want them silenced is whoever took it.");
}
