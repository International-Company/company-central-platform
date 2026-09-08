namespace CCP.Modules.Identity.Domain.Users;

/// <summary>
/// The lifecycle state of an account.
/// <para>
/// Only <see cref="Active"/> may authenticate. Every other state is a deliberate
/// refusal, and the reason is recorded so a support conversation can explain
/// what happened without anyone reading the database by hand.
/// </para>
/// </summary>
public enum UserStatus
{
    /// <summary>Created but not yet activated; cannot sign in.</summary>
    PendingActivation = 1,

    /// <summary>Normal, may authenticate.</summary>
    Active = 2,

    /// <summary>
    /// Turned off by an administrator. Deliberate and indefinite — distinct
    /// from a lockout, which is automatic and temporary.
    /// </summary>
    Disabled = 3,

    /// <summary>
    /// Locked automatically after repeated failed sign-in attempts. Temporary,
    /// and clears itself once the lockout window passes.
    /// </summary>
    Locked = 4
}
