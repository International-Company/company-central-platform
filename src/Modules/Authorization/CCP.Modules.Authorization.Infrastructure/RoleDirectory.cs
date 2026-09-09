using CCP.Modules.Authorization.Contracts;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Authorization.Infrastructure;

/// <summary>
/// Answers "who holds this role", and nothing else.
/// <para>
/// The whole of what another module may know about authorization
/// (ARCHITECTURE.md §6.2). Workflow uses it to turn "whoever approves
/// purchases" into people with inboxes.
/// </para>
/// </summary>
public sealed class RoleDirectory(AuthorizationDbContext dbContext) : IRoleDirectory
{
    public async Task<IReadOnlyList<Guid>> GetUserIdsWithRoleAsync(
        Guid roleId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Live grants only: not revoked, not expired, and on a role that is
        // still active. A task assigned from a lapsed grant is a task its
        // recipient has no business doing.
        return await dbContext.Assignments
            .AsNoTracking()
            .Where(a => a.RoleId == roleId
                     && a.RevokedAt == null
                     && (a.ExpiresAt == null || a.ExpiresAt > now))
            .Join(
                dbContext.Roles.AsNoTracking().Where(r => r.IsActive),
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, _) => assignment.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
