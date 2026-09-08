using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Identity.Domain.Credentials;

/// <summary>
/// A user's current password hash, held separately from the user itself.
/// <para>
/// The separation is deliberate: listing users, or loading one to render a
/// profile, must not bring a password hash into memory or anywhere near a DTO.
/// Only the sign-in and password-change paths load this entity at all.
/// </para>
/// <para>
/// The plaintext password never exists on this type, in any form, at any point.
/// </para>
/// </summary>
public sealed class UserCredential : Entity
{
    private UserCredential() { }

    private UserCredential(Guid id, Guid userId, string passwordHash, string algorithm, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        PasswordHash = passwordHash;
        Algorithm = algorithm;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid UserId { get; private set; }

    /// <summary>
    /// The encoded hash, including its salt and parameters. Never returned by
    /// any API, never logged, never placed in an audit record.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// Which algorithm produced the hash, recorded so credentials can be
    /// migrated to a stronger one later without invalidating everyone's
    /// password at once.
    /// </summary>
    public string Algorithm { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserCredential Create(Guid userId, string passwordHash, string algorithm, DateTimeOffset now)
        => new(Uuid7.NewGuid(now), userId, passwordHash, algorithm, now);

    public void Update(string passwordHash, string algorithm, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        Algorithm = algorithm;
        UpdatedAt = now;
    }
}

/// <summary>
/// A previously used password hash, kept so the policy can refuse reuse.
/// <para>
/// Only hashes are stored, and only a bounded number per user — history is a
/// liability as well as a control, so it is pruned rather than kept forever.
/// </para>
/// </summary>
public sealed class PasswordHistoryEntry : Entity
{
    private PasswordHistoryEntry() { }

    private PasswordHistoryEntry(Guid id, Guid userId, string passwordHash, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        PasswordHash = passwordHash;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static PasswordHistoryEntry Create(Guid userId, string passwordHash, DateTimeOffset now)
        => new(Uuid7.NewGuid(now), userId, passwordHash, now);
}
