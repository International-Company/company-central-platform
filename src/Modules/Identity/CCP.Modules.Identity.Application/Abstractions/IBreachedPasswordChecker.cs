namespace CCP.Modules.Identity.Application.Abstractions;

/// <summary>
/// Screens a password against known-breached password lists.
/// <para>
/// The single most effective password control available: credential stuffing
/// uses passwords from exactly these lists. A twelve-character password that
/// appears in a breach corpus is weaker in practice than a shorter one that
/// does not.
/// </para>
/// <para>
/// An interface so the source can be a local list, a k-anonymity API, or
/// nothing at all in development. When a check cannot be completed, an
/// implementation must <b>allow</b> the password rather than block the user —
/// an unavailable screening service must not stop people signing up. That
/// choice is recorded here so it is a decision rather than an accident.
/// </para>
/// </summary>
public interface IBreachedPasswordChecker
{
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default);
}
