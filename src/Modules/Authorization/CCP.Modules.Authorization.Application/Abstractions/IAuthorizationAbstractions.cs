using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.Application.Abstractions;

/// <summary>The Authorization module's unit of work.</summary>
public interface IAuthorizationUnitOfWork : IUnitOfWork;

/// <summary>The Authorization module's outbox, bound to its own DbContext.</summary>
public interface IAuthorizationOutbox : IOutbox;

/// <summary>
/// The monotonic stamp that makes cached permissions safe.
/// <para>
/// Any change to roles, role permissions, grants or the organizational tree
/// bumps this. A cached permission set carries the version it was computed at;
/// if the current version differs, it is discarded (ADR-007 §14.4).
/// </para>
/// <para>
/// <b>It lives in the database, not in memory.</b> With several application
/// instances, an in-memory counter would let instance B keep serving revoked
/// access because instance A did the revoking. Reading one indexed row per
/// request is the price of that being correct, and it is far cheaper than
/// recomputing the full permission join every time.
/// </para>
/// </summary>
public interface IPermissionVersionStore
{
    /// <summary>The current stamp.</summary>
    Task<long> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Bumps the stamp, invalidating every cached permission set everywhere.
    /// <para>
    /// Deliberately global rather than per-user. Over-invalidating on a role
    /// change costs one recomputation per active user; under-invalidating means
    /// someone keeps access that was revoked. The asymmetry is not close.
    /// </para>
    /// </summary>
    Task BumpAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves what a user may do.
/// <para>
/// The single entry point the <c>RequirePermission</c> handler uses, and
/// therefore the hottest path in the Platform.
/// </para>
/// </summary>
public interface IPermissionResolver
{
    /// <summary>
    /// Every permission the user holds, with scopes already resolved to concrete
    /// unit paths so evaluation needs no further lookups.
    /// </summary>
    Task<EffectivePermissions> GetEffectivePermissionsAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The user's own organizational unit path, used to resolve scopes that
    /// follow the holder. Null when the user has no employee record.
    /// </summary>
    Task<string?> GetUserUnitPathAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Evaluates one permission for one user, filter included.</summary>
    Task<AccessDecision> EvaluateAsync(
        Guid userId, string permissionName, CancellationToken cancellationToken = default);

    /// <summary>Everything a registered application may do, acting as itself.</summary>
    Task<EffectivePermissions> GetEffectivePermissionsForApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>Evaluates one permission for an application acting as itself.</summary>
    Task<AccessDecision> EvaluateForApplicationAsync(
        Guid applicationId, string permissionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates one permission for an application acting on behalf of a person.
    /// <para>
    /// The intersection of the two: a delegated call may do only what the
    /// application is trusted with and what the person is entitled to.
    /// </para>
    /// </summary>
    Task<AccessDecision> EvaluateDelegatedAsync(
        Guid applicationId, Guid userId, string permissionName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the organizational facts Authorization needs, through Organization's
/// contract rather than its database (ARCHITECTURE.md §6.2).
/// </summary>
public interface IOrganizationScopeReader
{
    /// <summary>The materialized path of a unit, or null if it does not exist.</summary>
    Task<string?> GetUnitPathAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>The unit path of the employee linked to a user, or null.</summary>
    Task<string?> GetUnitPathForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for the Authorization module.</summary>
public interface IAuthorizationRepository
{
    // --- Applications ------------------------------------------------------

    Task<RegisteredApplication?> FindApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken = default);

    Task<RegisteredApplication?> FindApplicationByCodeAsync(
        string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync(
        CancellationToken cancellationToken = default);

    void AddApplication(RegisteredApplication application);

    // --- Permissions -------------------------------------------------------

    Task<Permission?> FindPermissionAsync(Guid permissionId, CancellationToken cancellationToken = default);

    Task<Permission?> FindPermissionByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Permission>> GetPermissionsForApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Permission>> GetAllPermissionsAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>
    /// The permissions a role carries, by name.
    /// <para>
    /// Needed by the anti-escalation check, which has to know what granting a
    /// role actually hands over. Checking the role rather than its contents
    /// would let somebody pass on a role full of permissions they have never
    /// held themselves.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Permission>> GetPermissionsForRoleAsync(
        Guid roleId, CancellationToken cancellationToken = default);

    void AddPermission(Permission permission);

    // --- Roles -------------------------------------------------------------

    Task<Role?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What this role looked like when it was read.
    /// <para>
    /// PostgreSQL's own row version, which every table already has, so nothing
    /// was added to the schema to carry it. Opaque outside a comparison.
    /// </para>
    /// </summary>
    long VersionOf(Role role);

    /// <summary>
    /// Says which version the caller believed they were changing.
    /// <para>
    /// The save then fails rather than succeeding against a row somebody else
    /// has moved on. Without it the second of two concurrent edits silently
    /// discards the first, in the table that decides what everyone in the
    /// company can do.
    /// </para>
    /// </summary>
    void ExpectVersion(Role role, long version);

    Task<Role?> FindRoleByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Role>> GetRolesAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<bool> RoleCodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void AddRole(Role role);

    // --- Grants ------------------------------------------------------------

    Task<UserRoleAssignment?> FindAssignmentAsync(
        Guid assignmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserRoleAssignment>> GetAssignmentsForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<bool> AssignmentExistsAsync(
        Guid userId, Guid roleId, Domain.Scopes.ScopeType scopeType, Guid? scopeUnitId,
        CancellationToken cancellationToken = default);

    void AddAssignment(UserRoleAssignment assignment);

    /// <summary>
    /// The raw grants behind a user's effective permissions: every active
    /// assignment, its role's permissions, and the scope each was granted at.
    /// One query rather than a walk, because this runs on the hot path.
    /// </summary>
    Task<IReadOnlyList<GrantRow>> GetGrantsForUserAsync(
        Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>
/// One row of the permission join: a permission a user holds, and the scope it
/// came with. Flat by design — the resolver groups it, and a nested shape would
/// mean either several queries or a more expensive one.
/// </summary>
/// <param name="PermissionName">The full permission name.</param>
/// <param name="ScopeType">How far the grant reaches.</param>
/// <param name="ScopeUnitId">The anchor unit, or null to follow the holder.</param>
public sealed record GrantRow(string PermissionName, ScopeType ScopeType, Guid? ScopeUnitId);
