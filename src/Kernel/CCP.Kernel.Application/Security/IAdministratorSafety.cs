namespace CCP.Kernel.Application.Security;

/// <summary>
/// Whether the Platform would still have somebody able to hand out access.
/// <para>
/// <b>There is no way back from losing the last administrator.</b> Bootstrapping
/// refuses to run once any user exists — correctly, because an endpoint that
/// creates an administrator on an empty database is a back door on a full one —
/// so a Platform whose last role-granting account is disabled or stripped cannot
/// be recovered through any interface it offers. Somebody with database access
/// has to write the row by hand.
/// </para>
/// <para>
/// The answer is therefore a guard rather than a recovery path. A recovery path
/// is a way in, and a way in is a way in for whoever finds it; refusing the
/// operation that would strand the Platform costs an administrator one confusing
/// afternoon and costs an attacker the whole technique.
/// </para>
/// <para>
/// <b>A kernel seam, because two modules need the same answer and neither may
/// ask the other.</b> Identity refuses to disable the account; Authorization
/// refuses to revoke the grant, empty the role or switch the role off. The
/// question is one; the module that can answer it is Authorization, and the
/// contract lives here — the same shape as <c>IAuditTrail</c> and
/// <c>IJobJournal</c>.
/// </para>
/// </summary>
public interface IAdministratorSafety
{
    /// <summary>
    /// Whether this user is the only one who can still grant roles.
    /// <para>
    /// Asked before an account is disabled. True means the operation would leave
    /// nobody able to hand out access to anybody, ever, through any endpoint the
    /// Platform has.
    /// </para>
    /// <para>
    /// "Can grant roles" means holding <c>platform.roles.assign</c> through a
    /// live assignment of an active role. Not "is an administrator" — there is
    /// no such flag, and inventing one would put a second definition of
    /// privilege next to the one the Platform actually enforces.
    /// </para>
    /// </summary>
    Task<bool> IsTheLastGrantingUserAsync(
        Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The answer for a host with no Authorization module: no.
/// <para>
/// Not a weakened guard but an inapplicable one. Without Authorization there are
/// no role grants to be the last of, so nothing an operation here could do would
/// strand anybody. The Authorization module replaces this at registration, and
/// the last registration wins.
/// </para>
/// </summary>
public sealed class NoGrantsToStrand : IAdministratorSafety
{
    public Task<bool> IsTheLastGrantingUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
