using CCP.Kernel.Results;

namespace CCP.Modules.Organization.Domain.Employees;

/// <summary>
/// Walks reporting lines, and refuses to create loops in them.
/// <para>
/// Unlike the unit hierarchy, reporting lines have no materialized path — a
/// manager change is far more common than a unit move, and maintaining a path
/// would mean rewriting a chain on every one of them. So the cycle check walks
/// the chain instead, which is cheap because chains are short: an organization
/// with a hundred levels of management does not exist.
/// </para>
/// <para>
/// <b>Why cycles must be impossible.</b> The workflow engine resolves "the
/// requester's manager" by walking this chain (ARCHITECTURE.md §16.3). A loop
/// would make that walk run forever, or — with a naive depth cap — silently
/// route an approval to the wrong person. Neither failure is acceptable in an
/// approval path, so the loop is prevented at the point it would be created
/// rather than defended against at every read.
/// </para>
/// </summary>
public static class ManagementChain
{
    /// <summary>
    /// A hard ceiling on how far the walk will go.
    /// <para>
    /// A guard against corrupt data, not against legitimate depth. If a cycle
    /// somehow exists — written directly to the database, say — this stops the
    /// walk rather than hanging the request.
    /// </para>
    /// </summary>
    public const int MaxDepth = 100;

    /// <summary>
    /// Whether assigning <paramref name="candidateManagerId"/> to
    /// <paramref name="employeeId"/> would create a loop.
    /// </summary>
    /// <param name="employeeId">The employee whose manager is being set.</param>
    /// <param name="candidateManagerId">The proposed manager.</param>
    /// <param name="managerOf">
    /// Resolves an employee's current manager id. Supplied by the use case,
    /// which has database access; this type deliberately has none.
    /// </param>
    public static async Task<Result> ValidateAssignmentAsync(
        Guid employeeId,
        Guid? candidateManagerId,
        Func<Guid, CancellationToken, Task<Guid?>> managerOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(managerOf);

        if (candidateManagerId is not { } managerId)
        {
            // Clearing the manager can never create a cycle.
            return Result.Success();
        }

        if (managerId == employeeId)
        {
            return Result.Failure(OrganizationErrors.EmployeeCannotManageThemselves);
        }

        // Walk up from the proposed manager. If the employee appears anywhere in
        // that chain, the proposed manager already reports to them — directly or
        // through others — and the assignment would close the loop.
        Guid? current = managerId;
        var visited = new HashSet<Guid> { employeeId };

        for (int depth = 0; depth < MaxDepth && current is { } currentId; depth++)
        {
            if (currentId == employeeId)
            {
                return Result.Failure(OrganizationErrors.ManagementCycle);
            }

            // Also stops if the existing data already contains a loop, rather
            // than walking it forever.
            if (!visited.Add(currentId))
            {
                return Result.Failure(OrganizationErrors.ManagementCycle);
            }

            current = await managerOf(currentId, cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// The chain of managers above an employee, nearest first.
    /// <para>
    /// Used by workflow to find an approver, and by the administration portal to
    /// show a reporting line. Stops at <see cref="MaxDepth"/> or on a repeat, so
    /// corrupt data degrades into a truncated answer rather than a hang.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> WalkUpAsync(
        Guid employeeId,
        Func<Guid, CancellationToken, Task<Guid?>> managerOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(managerOf);

        var chain = new List<Guid>();
        var visited = new HashSet<Guid> { employeeId };

        Guid? current = await managerOf(employeeId, cancellationToken);

        for (int depth = 0; depth < MaxDepth && current is { } currentId; depth++)
        {
            if (!visited.Add(currentId))
            {
                break;
            }

            chain.Add(currentId);
            current = await managerOf(currentId, cancellationToken);
        }

        return chain;
    }
}
