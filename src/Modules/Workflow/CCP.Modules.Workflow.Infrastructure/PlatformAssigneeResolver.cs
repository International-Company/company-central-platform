using CCP.Modules.Authorization.Contracts;
using CCP.Modules.Organization.Contracts;
using CCP.Modules.Workflow.Application.Abstractions;
using CCP.Modules.Workflow.Domain.Definitions;

namespace CCP.Modules.Workflow.Infrastructure;

/// <summary>
/// Turns a step's rule into the people who must act, using the Platform's own
/// modules.
/// <para>
/// <b>This class is why Workflow does not reference Organization or
/// Authorization.</b> The engine holds the contract; this holds the answer, and
/// it lives in the infrastructure layer where knowing about other modules is
/// the composition root's business rather than the engine's (§6.2). Lift the
/// engine into another product and this is the only file that needs rewriting.
/// </para>
/// <para>
/// It reaches both modules through their narrow public surfaces —
/// <see cref="IOrganizationDirectory"/> and <see cref="IRoleDirectory"/> —
/// never their schemas, and holds no foreign key into either.
/// </para>
/// <para>
/// Every strategy answers "which person, by their place in the company".
/// None of them answers "which person, given what is being approved": that
/// question is the calling application's, and its answer arrives through
/// <see cref="AssigneeStrategy.SuppliedByCaller"/>.
/// </para>
/// </summary>
public sealed class PlatformAssigneeResolver(
    IOrganizationDirectory organization,
    IRoleDirectory roles) : IAssigneeResolver
{
    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        AssigneeRule rule,
        Guid requesterUserId,
        IReadOnlyList<Guid> suppliedAssignees,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.Strategy switch
        {
            AssigneeStrategy.User =>
                rule.UserId is { } user ? [user] : [],

            AssigneeStrategy.Role =>
                rule.RoleId is { } role
                    ? await roles.GetUserIdsWithRoleAsync(role, cancellationToken)
                    : [],

            AssigneeStrategy.Position =>
                rule.PositionId is { } position
                    ? await organization.GetUserIdsInPositionAsync(position, cancellationToken)
                    : [],

            AssigneeStrategy.RequesterManager =>
                await ResolveManagerAsync(requesterUserId, cancellationToken),

            AssigneeStrategy.UnitHead =>
                rule.UnitId is { } unit
                    ? await ResolveUnitHeadAsync(unit, cancellationToken)
                    : [],

            // Whatever the application worked out. This is where routing that
            // depends on business data lives — outside the engine, decided by
            // the system that understands what is being approved.
            AssigneeStrategy.SuppliedByCaller => suppliedAssignees,

            _ => []
        };
    }

    /// <summary>
    /// The requester's direct manager, as a user account.
    /// <para>
    /// Resolved now rather than when the request was filed, so a request that
    /// has waited three weeks reaches the manager the person has today.
    /// </para>
    /// <para>
    /// Returns nobody when the requester has no employee record — a service
    /// account or a contractor — or when their manager has no account to act
    /// with. The engine treats "nobody" as a failure and refuses to enter the
    /// step, which is the right outcome: a request routed to an empty set waits
    /// forever, and silence is the worst way to find that out.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveManagerAsync(
        Guid requesterUserId,
        CancellationToken cancellationToken)
    {
        Guid? employeeId = await organization.GetEmployeeIdForUserAsync(
            requesterUserId, cancellationToken);

        if (employeeId is not { } employee)
        {
            return [];
        }

        IReadOnlyList<Guid> chain = await organization.GetManagementChainAsync(
            employee, cancellationToken);

        if (chain.Count == 0)
        {
            return [];
        }

        // Nearest first, so the direct manager is the head of the chain. The
        // rest is deliberately unused: escalation walks further up, approval
        // does not skip a level.
        Guid? managerUser = await organization.GetUserIdForEmployeeAsync(
            chain[0], cancellationToken);

        return managerUser is { } account ? [account] : [];
    }

    private async Task<IReadOnlyList<Guid>> ResolveUnitHeadAsync(
        Guid unitId,
        CancellationToken cancellationToken)
    {
        Guid? head = await organization.GetUnitHeadUserIdAsync(unitId, cancellationToken);

        return head is { } account ? [account] : [];
    }
}
