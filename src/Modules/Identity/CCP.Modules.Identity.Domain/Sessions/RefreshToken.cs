using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Identity.Domain.Sessions;

/// <summary>
/// A single-use refresh token.
/// <para>
/// <b>Rotation with reuse detection</b> is the highest-value control in the
/// authentication design (ADR-006). Each refresh issues a new token and marks
/// the old one used. If a used token is ever presented again, one of two things
/// happened: the token was stolen and the thief is using it, or it was stolen
/// and the victim is. Either way the session family is compromised, so all of it
/// is revoked and a security event is raised.
/// </para>
/// <para>
/// Without rotation, a stolen refresh token is a silent, long-lived compromise.
/// With it, the theft becomes a detectable incident.
/// </para>
/// <para>
/// Only the SHA-256 hash of the token is stored. A database backup that leaks
/// therefore leaks no usable token — the same reasoning that applies to
/// passwords applies here.
/// </para>
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    private RefreshToken(
        Guid id,
        Guid sessionId,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        SessionId = sessionId;
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid SessionId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>
    /// Groups every token descended from one sign-in. Reuse of any member
    /// invalidates the whole family, which is what stops a thief from simply
    /// continuing down the chain.
    /// </summary>
    public Guid FamilyId { get; private set; }

    /// <summary>SHA-256 of the token. The token itself is never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When this token was exchanged. Non-null means it is spent.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>The token issued in exchange, for tracing a family's chain.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public bool IsUsed => UsedAt is not null;

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Begins a new family, at sign-in.</summary>
    public static RefreshToken Issue(
        Guid sessionId,
        Guid userId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime)
        => new(Uuid7.NewGuid(now), sessionId, userId, Uuid7.NewGuid(now), tokenHash, now, now.Add(lifetime));

    /// <summary>Continues an existing family, at refresh.</summary>
    public static RefreshToken IssueInFamily(
        Guid sessionId,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime)
        => new(Uuid7.NewGuid(now), sessionId, userId, familyId, tokenHash, now, now.Add(lifetime));

    /// <summary>
    /// Usable only if unused, unrevoked and unexpired. All three conditions,
    /// every time — a token that fails any of them is not a valid credential.
    /// </summary>
    public bool IsUsable(DateTimeOffset now)
        => !IsUsed && !IsRevoked && now < ExpiresAt;

    /// <summary>Marks this token spent and records its successor.</summary>
    public void MarkUsed(Guid replacedByTokenId, DateTimeOffset now)
    {
        UsedAt = now;
        ReplacedByTokenId = replacedByTokenId;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }
}
