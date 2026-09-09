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
}
