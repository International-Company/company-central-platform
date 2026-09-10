namespace CCP.Modules.Identity.Contracts;

/// <summary>
/// The Identity module's public surface for other modules.
/// <para>
/// Deliberately narrow: an account's address and its language, and nothing
/// else. Notifications needs to know where to send and in what language; it has
/// no business knowing about credentials, sessions, lockouts or anything else
/// Identity holds (ARCHITECTURE.md §6.2).
/// </para>
/// <para>
/// Notably absent: any way to read a password hash, a token, or a security
/// answer. A broad "get me the user" method would put all of that one property
/// access away from a module that has no reason to see it.
/// </para>
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// The account's email address, or null.
    /// <para>
    /// Null is a real answer: a service account may have none, and a disabled
    /// account should not be written to. Callers treat it as "cannot deliver
    /// here" rather than as an error.
    /// </para>
    /// </summary>
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The account's display name, for addressing a message.</summary>
    Task<string?> GetDisplayNameAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The language this person chose, or null if they never chose one.
    /// <para>
    /// <b>Null rather than a default.</b> Identity does not know what the
    /// company speaks, and inventing an answer here would put that decision in
    /// the wrong module — the caller knows its own fallback and this one is
    /// honest about not knowing.
    /// </para>
    /// </summary>
    Task<string?> GetPreferredLocaleAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this account could sign in right now.
    /// <para>
    /// Asked by Authorization before an application is allowed to act on
    /// somebody's behalf. Without it, delegation would happily mint a token
    /// naming a disabled account, an account that never existed, or a typo — and
    /// the audit trail would then name a person who had nothing to do with it.
    /// </para>
    /// <para>
    /// A single boolean on purpose. "Why not" is Identity's business: a caller
    /// that learned the difference between disabled, locked and non-existent
    /// would have an account-enumeration oracle.
    /// </para>
    /// </summary>
    Task<bool> CanSignInAsync(Guid userId, CancellationToken cancellationToken = default);
}
