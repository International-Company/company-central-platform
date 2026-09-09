using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Scopes;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Authorization.Infrastructure.Persistence;

/// <summary>
/// Reads and writes application credentials and application grants.
/// <para>
/// Same schema, same DbContext, same transaction as the rest of Authorization —
/// a separate class only because the main repository interface is already long
/// enough to be hard to read.
/// </para>
/// </summary>
public sealed class ApplicationRepository(AuthorizationDbContext dbContext) : IApplicationRepository
{
    // --- Credentials --------------------------------------------------------

    /// <summary>
    /// Tracked, not <c>AsNoTracking</c>: a successful exchange stamps
    /// <c>LastUsedAt</c> on the row it just read, and that is the field that
    /// makes finishing a rotation possible.
    /// </summary>
    public async Task<ApplicationCredential?> FindCredentialByClientIdAsync(
        string clientId, CancellationToken cancellationToken = default)
        => await dbContext.ApplicationCredentials
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

    public async Task<ApplicationCredential?> FindCredentialAsync(
        Guid credentialId, CancellationToken cancellationToken = default)
        => await dbContext.ApplicationCredentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, cancellationToken);

    /// <summary>
    /// Every credential an application has ever held, newest first.
    /// <para>
    /// Revoked ones stay in the list. "This key was withdrawn in March" is the
    /// answer somebody needs during an incident, and hiding it would leave them
    /// wondering whether it ever existed.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ApplicationCredential>> GetCredentialsAsync(
        Guid applicationId, CancellationToken cancellationToken = default)
        => await dbContext.ApplicationCredentials
            .AsNoTracking()
            .Where(c => c.ApplicationId == applicationId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public void AddCredential(ApplicationCredential credential)
        => dbContext.ApplicationCredentials.Add(credential);

    // --- Grants -------------------------------------------------------------

    public async Task<ApplicationRoleAssignment?> FindApplicationAssignmentAsync(
        Guid assignmentId, CancellationToken cancellationToken = default)
        => await dbContext.ApplicationAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

    public async Task<IReadOnlyList<ApplicationRoleAssignment>> GetAssignmentsForApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken = default)
        => await dbContext.ApplicationAssignments
            .AsNoTracking()
            .Where(a => a.ApplicationId == applicationId)
            .OrderByDescending(a => a.GrantedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> ApplicationAssignmentExistsAsync(
        Guid applicationId,
        Guid roleId,
        ScopeType scopeType,
        Guid? scopeUnitId,
        CancellationToken cancellationToken = default)
        => await dbContext.ApplicationAssignments
            .AsNoTracking()
            .AnyAsync(
                a => a.ApplicationId == applicationId
                     && a.RoleId == roleId
                     && a.ScopeType == scopeType
                     && a.ScopeUnitId == scopeUnitId
                     && a.RevokedAt == null,
                cancellationToken);

    public void AddApplicationAssignment(ApplicationRoleAssignment assignment)
        => dbContext.ApplicationAssignments.Add(assignment);

    /// <summary>
    /// The permission join for an application.
    /// <para>
    /// Deliberately the same query as the one for a user, from the other grant
    /// table. Any divergence between these two would be a difference in what a
    /// machine may do versus what a person may do, arrived at by accident.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<GrantRow>> GetGrantsForApplicationAsync(
        Guid applicationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => await (
            from assignment in dbContext.ApplicationAssignments.AsNoTracking()
            where assignment.ApplicationId == applicationId
                  && assignment.RevokedAt == null
                  && (assignment.ExpiresAt == null || assignment.ExpiresAt > now)
            join application in dbContext.Applications.AsNoTracking()
                on assignment.ApplicationId equals application.Id

            // A disabled application holds nothing, whatever its grants say.
            // Disabling is the emergency stop, and a stop that left permissions
            // working would not be one.
            where application.IsActive
            join role in dbContext.Roles.AsNoTracking()
                on assignment.RoleId equals role.Id
            where role.IsActive
            join rolePermission in dbContext.RolePermissions.AsNoTracking()
                on role.Id equals rolePermission.RoleId
            join permission in dbContext.Permissions.AsNoTracking()
                on rolePermission.PermissionId equals permission.Id
            where permission.IsActive
            select new GrantRow(permission.Name, assignment.ScopeType, assignment.ScopeUnitId))
            .ToListAsync(cancellationToken);
}
