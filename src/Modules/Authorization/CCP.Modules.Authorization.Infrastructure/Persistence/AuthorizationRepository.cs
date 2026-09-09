using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Authorization.Infrastructure.Persistence;

/// <summary>EF Core implementation of the Authorization module's persistence port.</summary>
public sealed class AuthorizationRepository(AuthorizationDbContext dbContext) : IAuthorizationRepository
{
    // --- Applications ------------------------------------------------------

    public Task<RegisteredApplication?> FindApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken = default)
        => dbContext.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

    public Task<RegisteredApplication?> FindApplicationByCodeAsync(
        string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToLowerInvariant();

        return dbContext.Applications.FirstOrDefaultAsync(a => a.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync(
        CancellationToken cancellationToken = default)
        => await dbContext.Applications.AsNoTracking().OrderBy(a => a.Code).ToListAsync(cancellationToken);

    public void AddApplication(RegisteredApplication application)
        => dbContext.Applications.Add(application);

    // --- Permissions -------------------------------------------------------

    public Task<Permission?> FindPermissionAsync(
        Guid permissionId, CancellationToken cancellationToken = default)
        => dbContext.Permissions.FirstOrDefaultAsync(p => p.Id == permissionId, cancellationToken);

    public Task<Permission?> FindPermissionByNameAsync(
        string name, CancellationToken cancellationToken = default)
    {
        string normalized = name.Trim().ToLowerInvariant();

        return dbContext.Permissions.FirstOrDefaultAsync(p => p.Name == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<Permission>> GetPermissionsForApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken = default)
        => await dbContext.Permissions
            .Where(p => p.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Permission>> GetAllPermissionsAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
        => await dbContext.Permissions
            .AsNoTracking()
            .Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.Application).ThenBy(p => p.Resource).ThenBy(p => p.Action)
            .ToListAsync(cancellationToken);

    public void AddPermission(Permission permission) => dbContext.Permissions.Add(permission);

    // --- Roles -------------------------------------------------------------

    /// <summary>
    /// Loads a role with its permissions. The collection is needed by the
    /// anti-escalation check, so loading it lazily would mean a query per
    /// permission during a grant.
    /// </summary>
    public Task<Role?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
        => dbContext.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);

    public Task<Role?> FindRoleByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToLowerInvariant();

        return dbContext.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> GetRolesAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
        => await dbContext.Roles
            .AsNoTracking()
            .Include(r => r.Permissions)
            .Where(r => includeInactive || r.IsActive)
            .OrderBy(r => r.Code)
            .ToListAsync(cancellationToken);

    public Task<bool> RoleCodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToLowerInvariant();

        return dbContext.Roles.AnyAsync(r => r.Code == normalized, cancellationToken);
    }

    public void AddRole(Role role) => dbContext.Roles.Add(role);

    // --- Grants ------------------------------------------------------------

    public Task<UserRoleAssignment?> FindAssignmentAsync(
        Guid assignmentId, CancellationToken cancellationToken = default)
        => dbContext.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

    public async Task<IReadOnlyList<UserRoleAssignment>> GetAssignmentsForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.Assignments
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.GrantedAt)
            .ToListAsync(cancellationToken);

    public Task<bool> AssignmentExistsAsync(
        Guid userId,
        Guid roleId,
        ScopeType scopeType,
        Guid? scopeUnitId,
        CancellationToken cancellationToken = default)
        => dbContext.Assignments.AnyAsync(
            a => a.UserId == userId
                 && a.RoleId == roleId
                 && a.ScopeType == scopeType
                 && a.ScopeUnitId == scopeUnitId
                 && a.RevokedAt == null,
            cancellationToken);

    public void AddAssignment(UserRoleAssignment assignment) => dbContext.Assignments.Add(assignment);

    /// <summary>
    /// The permission join behind every authorization decision.
    /// <para>
    /// <b>One query.</b> Assignments, their roles, those roles' permissions, and
    /// the scope each was granted at — joined in the database rather than walked
    /// in application code. Expired and revoked grants and inactive roles and
    /// permissions are all filtered out here, so nothing downstream has to
    /// remember to.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Permission>> GetPermissionsForRoleAsync(
        Guid roleId, CancellationToken cancellationToken = default)
        => await (
            from rolePermission in dbContext.RolePermissions.AsNoTracking()
            where rolePermission.RoleId == roleId
            join permission in dbContext.Permissions.AsNoTracking()
                on rolePermission.PermissionId equals permission.Id
            where permission.IsActive
            select permission)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GrantRow>> GetGrantsForUserAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => await (
            from assignment in dbContext.Assignments.AsNoTracking()
            where assignment.UserId == userId
                  && assignment.RevokedAt == null
                  && (assignment.ExpiresAt == null || assignment.ExpiresAt > now)
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
