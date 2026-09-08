using CCP.Kernel.Results;

namespace CCP.Modules.Security.Domain;

/// <summary>
/// Every failure the Security module can produce.
/// <para>
/// MFA errors are deliberately less uniform than sign-in errors. By the time
/// someone reaches an MFA challenge they have already proven the password, so
/// telling them their code was wrong reveals nothing an attacker did not already
/// have — and telling them nothing would make a wrong phone clock impossible to
/// diagnose.
/// </para>
/// </summary>
public static class SecurityErrors
{
    // --- Enrolment ---------------------------------------------------------

    public static readonly Error MfaAlreadyActive = Error.Conflict(
        "SECURITY.MFA_ALREADY_ACTIVE", "Two-factor authentication is already enabled.");

    public static readonly Error MfaNotActive = Error.Conflict(
        "SECURITY.MFA_NOT_ACTIVE", "Two-factor authentication is not enabled.");

    public static readonly Error MfaNotEnrolled = Error.NotFound(
        "SECURITY.MFA_NOT_ENROLLED", "No two-factor enrolment was found.");

    public static readonly Error EnrolmentNotPending = Error.Conflict(
        "SECURITY.ENROLMENT_NOT_PENDING",
        "This enrolment is not awaiting confirmation. Start a new one.");

    // --- Verification ------------------------------------------------------

    /// <summary>
    /// A wrong code.
    /// <para>
    /// <b>A rule violation (400), deliberately not 401.</b> The caller's session
    /// is perfectly valid — they mistyped six digits. A BFF that treats 401 as
    /// "the access token expired" would refresh, retry, fail again and sign the
    /// user out, so returning 401 here would turn a typo into a logout. The
    /// status code has to distinguish "your session is no good" from "that code
    /// is no good", because clients act on the difference automatically.
    /// </para>
    /// </summary>
    public static readonly Error InvalidCode = Error.Rule(
        "SECURITY.INVALID_CODE",
        "That code is not correct. Check your authenticator app, and that your device clock is accurate.");

    /// <summary>
    /// Too many failures. Distinguished from a wrong code because the user needs
    /// to know that trying again immediately will not help.
    /// </summary>
    public static readonly Error MfaLockedOut = Error.Rule(
        "SECURITY.MFA_LOCKED_OUT",
        "Too many incorrect codes. Use a recovery code, or contact an administrator.");

    public static readonly Error InvalidRecoveryCode = Error.Rule(
        "SECURITY.INVALID_RECOVERY_CODE", "That recovery code is not valid or has already been used.");

    // --- Step-up -----------------------------------------------------------

    /// <summary>
    /// The operation needs the second factor proven again, recently.
    /// <para>
    /// Returned rather than simply refusing, so a client can prompt for a code
    /// and retry rather than presenting a dead end.
    /// </para>
    /// </summary>
    public static readonly Error StepUpRequired = Error.Forbidden(
        "SECURITY.STEP_UP_REQUIRED",
        "This action requires you to confirm your identity again.");

    public static readonly Error StepUpNotAvailable = Error.Forbidden(
        "SECURITY.STEP_UP_NOT_AVAILABLE",
        "This action requires two-factor authentication, which is not enabled on your account.");

    // --- Policy ------------------------------------------------------------

    /// <summary>
    /// The account holds administrative permissions and policy requires MFA for
    /// them.
    /// <para>
    /// <b>Enforced at the privileged action, not at sign-in.</b> Refusing sign-in
    /// would lock an administrator out of the very system they must use to enrol,
    /// and would punish people for a policy change made while they were away.
    /// <c>[RequireStepUp]</c> is the enforcement; this error is what the client
    /// shows when the reason is a missing factor rather than a lapsed one.
    /// </para>
    /// </summary>
    public static readonly Error MfaRequiredByPolicy = Error.Forbidden(
        "SECURITY.MFA_REQUIRED_BY_POLICY",
        "Your account requires two-factor authentication. Enrol before continuing.");
}
