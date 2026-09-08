using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Security.Domain.Events;

/// <summary>How much attention an event deserves.</summary>
public enum SecuritySeverity
{
    /// <summary>Routine and expected. Recorded for the trail, not for anyone to read.</summary>
    Informational = 1,

    /// <summary>Unusual. Worth noticing in aggregate.</summary>
    Low = 2,

    /// <summary>Suspicious. Worth a person looking if it repeats.</summary>
    Medium = 3,

    /// <summary>Probably an attack, or a real compromise. Someone should look now.</summary>
    High = 4
}

/// <summary>
/// Something security-relevant that happened.
/// <para>
/// <b>Distinct from the audit trail, deliberately.</b> Audit answers "who
/// changed what" — a complete, immutable business record. This answers "is
/// something wrong" — a smaller, noisier stream shaped for detection rather than
/// evidence. Conflating them makes the audit trail unreadable and the security
/// signal undiscoverable, because a failed sign-in and an approved invoice have
/// nothing in common but a timestamp.
/// </para>
/// <para>
/// Never deleted (ARCHITECTURE.md §7.2.4). An attacker's first instinct after
/// entry is to tidy up, and a security log that can be edited is a security log
/// nobody can rely on.
/// </para>
/// </summary>
public sealed class SecurityEvent : Entity
{
    private SecurityEvent() { }

    private SecurityEvent(
        Guid id,
        string eventType,
        SecuritySeverity severity,
        Guid? userId,
        string? username,
        string? ipAddress,
        string? userAgent,
        string? details,
        string? correlationId,
        DateTimeOffset occurredAt)
        : base(id)
    {
        EventType = eventType;
        Severity = severity;
        UserId = userId;
        Username = username;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Details = details;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    /// <summary>Stable event type, e.g. <c>mfa.challenge_failed</c>.</summary>
    public string EventType { get; private set; } = string.Empty;

    public SecuritySeverity Severity { get; private set; }

    /// <summary>
    /// The account involved, when there is one. Null for events against a
    /// username that does not exist — which are exactly the ones that matter for
    /// detecting credential stuffing.
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// The username as it was. Stored alongside the id for the same reason audit
    /// does: an entry reading "user 8f3a… failed MFA" is not usable evidence once
    /// the account has been renamed or deleted.
    /// </summary>
    public string? Username { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    /// <summary>
    /// Context, as JSON.
    /// <para>
    /// <b>Never a credential.</b> No password, token, recovery code or TOTP
    /// secret reaches this field. A security log full of working credentials is
    /// a worse breach than whatever it was recording.
    /// </para>
    /// </summary>
    public string? Details { get; private set; }

    /// <summary>Ties the event to the request, its logs and its audit record.</summary>
    public string? CorrelationId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static SecurityEvent Record(
        string eventType,
        SecuritySeverity severity,
        DateTimeOffset now,
        Guid? userId = null,
        string? username = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? details = null,
        string? correlationId = null)
        => new(Uuid7.NewGuid(now), eventType, severity, userId, username,
               ipAddress, Truncate(userAgent, 512), Truncate(details, 4000), correlationId, now);

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>
/// The event types the Platform records.
/// <para>
/// Constants rather than free strings, so that a query for
/// <c>mfa.challenge_failed</c> cannot silently miss events someone recorded as
/// <c>mfa.challengeFailed</c>. Detection that depends on spelling is detection
/// that quietly stops working.
/// </para>
/// </summary>
public static class SecurityEventTypes
{
    // --- MFA ---------------------------------------------------------------

    public const string MfaEnrolmentStarted = "mfa.enrolment_started";
    public const string MfaEnrolled = "mfa.enrolled";
    public const string MfaDisabled = "mfa.disabled";
    public const string MfaChallengeSucceeded = "mfa.challenge_succeeded";
    public const string MfaChallengeFailed = "mfa.challenge_failed";
    public const string MfaLockedOut = "mfa.locked_out";
    public const string RecoveryCodeUsed = "mfa.recovery_code_used";
    public const string RecoveryCodesRegenerated = "mfa.recovery_codes_regenerated";

    /// <summary>
    /// The user has few recovery codes left. Worth telling them before they have
    /// none and a lost phone becomes an administrator's problem.
    /// </summary>
    public const string RecoveryCodesRunningLow = "mfa.recovery_codes_low";

    // --- Authentication ----------------------------------------------------

    public const string SignInFailed = "auth.sign_in_failed";
    public const string AccountLocked = "auth.account_locked";
    public const string NewDeviceSignIn = "auth.new_device";
    public const string RefreshTokenReuseDetected = "auth.refresh_token_reused";
    public const string PasswordChanged = "auth.password_changed";
    public const string PasswordResetRequested = "auth.password_reset_requested";

    // --- Authorization -----------------------------------------------------

    public const string PermissionDenied = "authz.permission_denied";
    public const string PrivilegeEscalationAttempted = "authz.escalation_attempted";
    public const string RoleGranted = "authz.role_granted";
    public const string RoleRevoked = "authz.role_revoked";

    // --- Rate limiting -----------------------------------------------------

    public const string RateLimitExceeded = "ratelimit.exceeded";

    // --- Step-up -----------------------------------------------------------

    public const string StepUpRequired = "stepup.required";
    public const string StepUpSatisfied = "stepup.satisfied";
}
