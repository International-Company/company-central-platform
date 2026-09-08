namespace CCP.Modules.Identity.Application.Abstractions;

/// <summary>
/// Hashes and verifies passwords.
/// <para>
/// An interface rather than a direct call, for two reasons. It keeps the
/// Application layer free of a cryptography dependency, and it makes the
/// algorithm replaceable: when Argon2id is eventually superseded,
/// <see cref="NeedsRehash"/> allows a transparent migration as users sign in,
/// rather than invalidating every password at once (ARCHITECTURE.md §7.2.1).
/// </para>
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Identifier of the algorithm and parameters, stored alongside the hash.</summary>
    string AlgorithmId { get; }

    /// <summary>Hashes a password. The result includes its own salt and parameters.</summary>
    string Hash(string password);

    /// <summary>
    /// Verifies a password against a hash.
    /// <para>
    /// Implementations must compare in constant time. A comparison that returns
    /// early on the first differing byte leaks information about the hash
    /// through timing.
    /// </para>
    /// </summary>
    bool Verify(string password, string hash);

    /// <summary>
    /// Whether the hash was produced with weaker parameters than the current
    /// configuration, and should be upgraded next time the password is known.
    /// </summary>
    bool NeedsRehash(string hash);
}
