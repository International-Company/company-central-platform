namespace CCP.Modules.Organization.Contracts;

/// <summary>
/// The Organization module's public surface for other modules.
/// <para>
/// This is the whole of what another module may know about the organization
/// (ARCHITECTURE.md §6.2). Authorization uses it to resolve scope; Workflow will
/// use it to find a requester's manager. Neither touches the
/// <c>organization</c> schema, and neither holds a foreign key into it — which
/// is what keeps the module extractable.
/// </para>
/// <para>
/// Kept deliberately narrow. Every method here is one another module genuinely
/// needs; a broad interface would invite coupling that looks harmless until
/// someone tries to move the module.
/// </para>
/// </summary>
public interface IOrganizationDirectory
{
    /// <summary>
    /// The materialized path of a unit, or null if it does not exist.
    /// <para>
    /// The path — not the id — because that is what a subtree query needs:
    /// "everything under this unit" is a prefix scan, and returning the id would
    /// only force the caller to come back for the path.
    /// </para>
    /// </summary>
    Task<string?> GetUnitPathAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The unit path of the employee linked to a user account, or null when the
    /// user has no employee record.
    /// <para>
    /// Null is normal, not exceptional: service accounts and contractors have
    /// accounts and no place in the organization. Callers must handle it, and
    /// authorization treats it as granting nothing rather than everything.
    /// </para>
    /// </summary>
    Task<string?> GetUnitPathForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The accounts of everyone whose employee record sits under any of these
    /// unit paths.
    /// <para>
    /// The bulk form of <see cref="GetUnitPathForUserAsync"/>, and it exists for
    /// a reason the per-user version cannot serve: a scoped <i>list</i> has to be
    /// narrowed before it is paged, or the page numbers and the total describe a
    /// set the caller is not allowed to see.
    /// </para>
    /// <para>
    /// Matched by prefix on the materialized path, so "this unit and everything
    /// under it" is one index scan rather than a tree walk.
    /// </para>
    /// <para>
    /// An employee with no account contributes nothing, and an account with no
    /// employee is absent — it has no place in the organization, and there is no
    /// unit it could be under.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUserIdsUnderAsync(
        IReadOnlyCollection<string> unitPathPrefixes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The employee id linked to a user account, or null.
    /// </summary>
    Task<Guid?> GetEmployeeIdForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The account an employee signs in with, or null.
    /// <para>
    /// The inverse of <see cref="GetEmployeeIdForUserAsync"/>. Workflow needs it
    /// because the management chain is expressed in employees while tasks are
    /// assigned to accounts, and an employee with no account cannot be given
    /// one — which is a real situation, not an error.
    /// </para>
    /// </summary>
    Task<Guid?> GetUserIdForEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The user accounts of everyone holding a job position.
    /// <para>
    /// Accounts, not employees: Workflow assigns tasks to people who sign in,
    /// and an employee with no account cannot act on one. Someone holding the
    /// position but having no account is therefore absent from this list rather
    /// than present and unable to do anything.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUserIdsInPositionAsync(
        Guid positionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The user account of whoever heads a unit, or null.
    /// <para>
    /// "Head" means the employee in the unit whom nobody else in that unit
    /// manages — the top of its own reporting line. Derived rather than stored,
    /// because a stored head is a field that goes stale the first time somebody
    /// leaves and nobody remembers to update it.
    /// </para>
    /// </summary>
    Task<Guid?> GetUnitHeadUserIdAsync(
        Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The chain of managers above an employee, nearest first.
    /// <para>
    /// Used by Workflow from Phase 8 to resolve "the requester's manager". It
    /// terminates on a repeat, so data that is already corrupt yields a
    /// truncated chain rather than hanging an approval.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetManagementChainAsync(
        Guid employeeId, CancellationToken cancellationToken = default);
}
