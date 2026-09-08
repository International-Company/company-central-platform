using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Identity.Domain.Credentials;

/// <summary>
/// A single-use, short-lived token permitting one password reset.
/// <para>
/// A reset token is a full account-takeover credential for as long as it lives,
/// so it is treated exactly like one:
/// </para>
/// <list type="bullet">
/// <item>Stored <b>hashed</b>, so a leaked database yields nothing usable.</item>
/// <item><b>Single use</b> — consumed the moment it succeeds.</item>
/// <item><b>Short-lived</b>, thirty minutes by default.</item>
/// <item>Issuing a new one <b>invalidates</b> any outstanding token for that
/// user, so a forgotten email in an inbox cannot be used later.</item>
/// </list>
/// </summary>
public sealed class PasswordResetToken : Entity
{
    private PasswordResetToken() { }

    private PasswordResetToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string? requestedFromIp)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        RequestedFromIp = requestedFromIp;
    }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 of the token. The token itself is never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the token was redeemed. Non-null means it is spent.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? InvalidatedAt { get; private set; }

    /// <summary>
    /// Where the reset was requested from. Recorded so a pattern of requests
    /// against many accounts from one address is visible.
    /// </summary>
    public string? RequestedFromIp { get; private set; }

    public bool IsUsed => UsedAt is not null;

    public static PasswordResetToken Issue(
        Guid userId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime,
        string? requestedFromIp)
        => new(Uuid7.NewGuid(now), userId, tokenHash, now, now.Add(lifetime), requestedFromIp);

    /// <summary>Usable only if unspent, uninvalidated and unexpired.</summary>
    public bool IsUsable(DateTimeOffset now)
        => UsedAt is null && InvalidatedAt is null && now < ExpiresAt;

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;

    /// <summary>
    /// Invalidates without redeeming. Used when a newer token is issued, and
    /// when the password changes by another route — a reset link sitting in an
    /// inbox must not survive the password it was meant to replace.
    /// </summary>
    public void Invalidate(DateTimeOffset now)
    {
        if (InvalidatedAt is null && UsedAt is null)
        {
            InvalidatedAt = now;
        }
    }
}
