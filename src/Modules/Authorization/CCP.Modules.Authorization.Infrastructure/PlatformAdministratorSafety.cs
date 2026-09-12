using CCP.Kernel.Application.Security;
using CCP.Kernel.Primitives;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Roles;

namespace CCP.Modules.Authorization.Infrastructure;

/// <summary>
/// Answers the kernel's question about the last administrator, because
/// Authorization is the module that knows.
/// <para>
/// Registered over the kernel's default, the same way the audit trail and the
/// settings reader are. Identity asks this before disabling an account and never
/// learns that Authorization exists.
/// </para>
/// </summary>
public sealed class PlatformAdministratorSafety(
    IAuthorizationRepository repository,
    IClock clock) : IAdministratorSafety
{
    public async Task<bool> IsTheLastGrantingUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GrantingAssignment> granting =
            await repository.GetGrantingAssignmentsAsync(
                AdministratorSafety.GrantPermission, clock.UtcNow, cancellationToken);

        // Nobody at all can grant roles. The Platform is already stranded, and
        // refusing this operation would not unstrand it -- it would only add a
        // confusing error to an unrelated action.
        if (granting.Count == 0)
        {
            return false;
        }

        return granting.All(assignment => assignment.UserId == userId);
    }
}
