using CCP.Kernel.Results;

namespace CCP.Modules.Identity.Domain.Users;

/// <summary>
/// Every failure the Identity module can produce, in one place.
/// <para>
/// Codes are part of the API contract (ADR-008): stable, never localized, and
/// safe for a client to branch on. Messages are human text and may be replaced
/// with a localized string at the boundary.
/// </para>
/// <para>
/// <b>Note the ones that are deliberately never returned to an anonymous
/// caller.</b> <see cref="AccountDisabled"/>, <see cref="AccountLocked"/> and
/// <see cref="AccountNotActivated"/> describe a real account, so returning them
/// from a sign-in endpoint would confirm the username exists. They are used
/// internally, recorded in audit and security events, and collapsed into
/// <see cref="InvalidCredentials"/> on the way out.
/// </para>
/// </summary>
public static class IdentityErrors
{
    // --- Authentication ----------------------------------------------------

    /// <summary>
    /// The single response for every failed sign-in, whatever the real cause.
    /// Uniform by design: an attacker must not be able to tell an unknown
    /// username from a wrong password, or from a disabled account.
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Unauthenticated(
        "IDENTITY.INVALID_CREDENTIALS",
        "The username or password is incorrect.");

    public static readonly Error AccountDisabled = Error.Unauthenticated(
        "IDENTITY.ACCOUNT_DISABLED",
        "This account has been disabled.");

    public static readonly Error AccountLocked = Error.Unauthenticated(
        "IDENTITY.ACCOUNT_LOCKED",
        "This account is temporarily locked after repeated failed sign-in attempts.");

    public static readonly Error AccountNotActivated = Error.Unauthenticated(
        "IDENTITY.ACCOUNT_NOT_ACTIVATED",
        "This account has not been activated.");

    public static readonly Error PasswordChangeRequired = Error.Forbidden(
        "IDENTITY.PASSWORD_CHANGE_REQUIRED",
        "The password must be changed before continuing.");

    // --- Sessions and tokens ----------------------------------------------

    public static readonly Error InvalidRefreshToken = Error.Unauthenticated(
        "IDENTITY.INVALID_REFRESH_TOKEN",
        "The refresh token is invalid or has expired.");

    /// <summary>
    /// A refresh token that had already been used was presented again. This is
    /// the signature of a stolen token, so the whole session family is revoked
    /// (ADR-006).
    /// </summary>
    public static readonly Error RefreshTokenReused = Error.Unauthenticated(
        "IDENTITY.REFRESH_TOKEN_REUSED",
        "The session has been ended for security reasons. Please sign in again.");

    public static readonly Error SessionNotFound = Error.NotFound(
        "IDENTITY.SESSION_NOT_FOUND",
        "The session does not exist.");

    public static readonly Error SessionExpired = Error.Unauthenticated(
        "IDENTITY.SESSION_EXPIRED",
        "The session has expired.");

    // --- Users -------------------------------------------------------------

    public static readonly Error UserNotFound = Error.NotFound(
        "IDENTITY.USER_NOT_FOUND",
        "The user does not exist.");

    public static readonly Error UsernameTaken = Error.Conflict(
        "IDENTITY.USERNAME_TAKEN",
        "That username is already in use.");

    public static readonly Error EmailTaken = Error.Conflict(
        "IDENTITY.EMAIL_TAKEN",
        "That email address is already in use.");

    public static readonly Error AlreadyDisabled = Error.Conflict(
        "IDENTITY.ALREADY_DISABLED",
        "The account is already disabled.");

    public static readonly Error AlreadyActive = Error.Conflict(
        "IDENTITY.ALREADY_ACTIVE",
        "The account is already active.");

    public static readonly Error CannotModifySelf = Error.Forbidden(
        "IDENTITY.CANNOT_MODIFY_SELF",
        "You cannot perform this action on your own account.");

    // --- Field validation --------------------------------------------------

    public static readonly Error UsernameRequired = Error.Validation(
        "IDENTITY.USERNAME_REQUIRED", "A username is required.", "username");

    public static readonly Error UsernameLength = Error.Validation(
        "IDENTITY.USERNAME_LENGTH", "The username must be between 3 and 64 characters.", "username");

    public static readonly Error UsernameCharacters = Error.Validation(
        "IDENTITY.USERNAME_CHARACTERS",
        "The username may contain only letters, digits, dot, hyphen and underscore.",
        "username");

    public static readonly Error EmailRequired = Error.Validation(
        "IDENTITY.EMAIL_REQUIRED", "An email address is required.", "email");

    public static readonly Error EmailInvalid = Error.Validation(
        "IDENTITY.EMAIL_INVALID", "The email address is not valid.", "email");

    public static readonly Error EmailTooLong = Error.Validation(
        "IDENTITY.EMAIL_TOO_LONG", "The email address is too long.", "email");

    public static readonly Error DisplayNameRequired = Error.Validation(
        "IDENTITY.DISPLAY_NAME_REQUIRED", "A display name is required.", "displayName");

    public static readonly Error DisplayNameTooLong = Error.Validation(
        "IDENTITY.DISPLAY_NAME_TOO_LONG", "The display name is too long.", "displayName");

    // --- Password policy ---------------------------------------------------

    public static readonly Error PasswordRequired = Error.Validation(
        "IDENTITY.PASSWORD_REQUIRED", "A password is required.", "password");

    public static Error PasswordTooShort(int minimumLength) => Error.Validation(
        "IDENTITY.PASSWORD_TOO_SHORT",
        $"The password must be at least {minimumLength} characters.",
        "password");

    public static Error PasswordTooLong(int maximumLength) => Error.Validation(
        "IDENTITY.PASSWORD_TOO_LONG",
        $"The password must be at most {maximumLength} characters.",
        "password");

    public static readonly Error PasswordReused = Error.Validation(
        "IDENTITY.PASSWORD_REUSED",
        "This password has been used recently. Choose a different one.",
        "password");

    public static readonly Error PasswordContainsUsername = Error.Validation(
        "IDENTITY.PASSWORD_CONTAINS_USERNAME",
        "The password must not contain the username.",
        "password");

    public static readonly Error PasswordBreached = Error.Validation(
        "IDENTITY.PASSWORD_BREACHED",
        "This password has appeared in a known data breach. Choose a different one.",
        "password");

    /// <summary>
    /// One error for every reset-token failure — unknown, spent, superseded or
    /// expired. Distinguishing them would tell whoever holds an old link
    /// whether it was ever valid, and for which account.
    /// </summary>
    public static readonly Error InvalidPasswordResetToken = Error.Unauthenticated(
        "IDENTITY.INVALID_PASSWORD_RESET_TOKEN",
        "This password reset link is invalid or has expired. Request a new one.");

    public static readonly Error CurrentPasswordIncorrect = Error.Validation(
        "IDENTITY.CURRENT_PASSWORD_INCORRECT",
        "The current password is incorrect.",
        "currentPassword");
}
