using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Identity.Domain.Sessions;

/// <summary>
/// One authenticated session: a user, on a device, from an address, with a
/// lifetime and a revocation switch.
/// <para>
/// Sessions are server-side state, deliberately. A purely stateless JWT design
/// cannot revoke anything before expiry, which means a signed-out user is not
/// actually signed out and a compromised session cannot be ended. Both are
/// unacceptable for the company's single front door (ADR-006).
/// </para>
/// </summary>
public sealed class Session : AggregateRoot
{
    private Session() { }

    private Session(
        Guid id,
        Guid userId,
        string? ipAddress,
        string? userAgent,
        string? deviceFingerprint,
        DateTimeOffset createdAt,
        DateTimeOffset absoluteExpiresAt)
        : base(id)
    {
        UserId = userId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        DeviceFingerprint = deviceFingerprint;
        CreatedAt = createdAt;
        LastActivityAt = createdAt;
        AbsoluteExpiresAt = absoluteExpiresAt;
    }

    public Guid UserId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    /// <summary>
    /// A coarse device identifier, used to tell a user which of their sessions
    /// is which and to notice a sign-in from somewhere new. Not a security
    /// control on its own — it is client-supplied and therefore forgeable.
    /// </summary>
    public string? DeviceFingerprint { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>
    /// The hard ceiling. A session ends at this point however active it has
    /// been, so a long-lived session cannot be extended indefinitely by use.
    /// </summary>
    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static Session Start(
        Guid userId,
        string? ipAddress,
        string? userAgent,
        string? deviceFingerprint,
        DateTimeOffset now,
        TimeSpan absoluteLifetime)
        => new(
            Uuid7.NewGuid(now),
            userId,
            ipAddress,
            Truncate(userAgent, 512),
            Truncate(deviceFingerprint, 128),
            now,
            now.Add(absoluteLifetime));

    /// <summary>
    /// Whether the session may still be used, considering revocation, the
    /// absolute ceiling and the idle timeout.
    /// </summary>
    public bool IsActive(DateTimeOffset now, TimeSpan idleTimeout)
        => !IsRevoked
        && now < AbsoluteExpiresAt
        && now - LastActivityAt < idleTimeout;

    public void Touch(DateTimeOffset now) => LastActivityAt = now;

    /// <summary>
    /// Ends the session. Idempotent: revoking an already-revoked session keeps
    /// the original reason and time, because the first revocation is the one
    /// that matters for an investigation.
    /// </summary>
    public void Revoke(string reason, DateTimeOffset now)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) ? null
         : value.Length <= maxLength ? value
         : value[..maxLength];
}

/// <summary>Why a session ended. Recorded for audit and for support.</summary>
public static class SessionRevocationReasons
{
    public const string UserSignedOut = "user_signed_out";
    public const string AdministratorRevoked = "administrator_revoked";
    public const string PasswordChanged = "password_changed";
    public const string AccountDisabled = "account_disabled";
    public const string RefreshTokenReuseDetected = "refresh_token_reuse_detected";
    public const string Expired = "expired";
}
