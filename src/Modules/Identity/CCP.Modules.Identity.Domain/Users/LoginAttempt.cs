using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Identity.Domain.Users;

/// <summary>
/// One sign-in attempt, successful or not.
/// <para>
/// Attempts against usernames that do not exist are recorded too, with a null
/// <see cref="UserId"/>. That is deliberate and important: a burst of failures
/// spread across many unknown usernames is exactly what credential stuffing
/// looks like, and it is invisible if only real accounts are logged.
/// </para>
/// <para>
/// This is operational history for the user's own "recent activity" view and
/// for lockout. It is not a substitute for the audit trail, which arrives in
/// Phase 6 and has stronger guarantees.
/// </para>
/// </summary>
public sealed class LoginAttempt : Entity
{
    private LoginAttempt() { }

    private LoginAttempt(
        Guid id,
        Guid? userId,
        string attemptedUsername,
        bool succeeded,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset occurredAt)
        : base(id)
    {
        UserId = userId;
        AttemptedUsername = attemptedUsername;
        Succeeded = succeeded;
        FailureReason = failureReason;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        OccurredAt = occurredAt;
    }

    public Guid? UserId { get; private set; }

    /// <summary>
    /// The username as supplied, truncated. Stored even when no such user
    /// exists — that is the point.
    /// </summary>
    public string AttemptedUsername { get; private set; } = string.Empty;

    public bool Succeeded { get; private set; }

    /// <summary>
    /// The real reason, for operators. Never returned to the caller, who always
    /// receives the uniform <c>IDENTITY.INVALID_CREDENTIALS</c>.
    /// </summary>
    public string? FailureReason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static LoginAttempt Success(
        Guid userId,
        string username,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now)
        => new(Uuid7.NewGuid(now), userId, Truncate(username, 64) ?? string.Empty, true, null, ipAddress,
               Truncate(userAgent, 512), now);

    public static LoginAttempt Failure(
        Guid? userId,
        string attemptedUsername,
        string reason,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now)
        => new(Uuid7.NewGuid(now), userId, Truncate(attemptedUsername, 64) ?? string.Empty, false, reason, ipAddress,
               Truncate(userAgent, 512), now);

    // Nullable annotations do not distinguish overloads, so one method handles
    // both the required username and the optional user agent.
    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>
/// Internal reasons a sign-in failed. Recorded on <see cref="LoginAttempt"/>
/// and in security events; never sent to the caller.
/// </summary>
public static class LoginFailureReasons
{
    public const string UnknownUsername = "unknown_username";
    public const string WrongPassword = "wrong_password";
    public const string AccountDisabled = "account_disabled";
    public const string AccountLocked = "account_locked";
    public const string AccountNotActivated = "account_not_activated";
}
