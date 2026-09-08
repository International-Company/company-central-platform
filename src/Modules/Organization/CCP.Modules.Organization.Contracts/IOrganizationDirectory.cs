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
    /// The employee id linked to a user account, or null.
    /// </summary>
    Task<Guid?> GetEmployeeIdForUserAsync(Guid userId, CancellationToken cancellationToken = default);

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
