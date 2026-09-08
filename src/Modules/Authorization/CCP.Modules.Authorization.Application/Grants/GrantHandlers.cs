using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Roles.Events;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.Application.Grants;

/// <summary>Granting a role to a user.</summary>
public sealed record GrantRoleCommand(
    Guid UserId,
    Guid RoleId,
    ScopeType ScopeType,
    Guid? ScopeUnitId,
    DateTimeOffset? ExpiresAt,
    Guid ActingUserId);

/// <summary>
/// Grants a role, enforcing the two rules that keep authorization from being
/// self-amplifying.
/// <para>
/// <b>1. No user may grant a permission they do not hold.</b> Without it, the
/// ability to grant <i>anything</i> becomes the ability to grant
/// <i>everything</i>: a junior administrator with <c>roles.assign</c> could hand
/// themselves — via a second account — every permission in the company. The
/// check compares the role's full permission set against the granter's own.
/// </para>
/// <para>
/// <b>2. No user may grant a scope wider than their own.</b> Otherwise someone
/// with department-level access could grant company-wide access, which is the
/// same escalation wearing a different hat.
/// </para>
/// <para>
/// Self-granting is refused by the domain (<see cref="UserRoleAssignment.Grant"/>),
/// since that needs only the two ids.
/// </para>
/// </summary>
public sealed class GrantRoleHandler(
    IAuthorizationRepository repository,
    IPermissionResolver resolver,
    IPermissionVersionStore versionStore,
    IAuthorizationOutbox outbox,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        GrantRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        Role? role = await repository.FindRoleAsync(command.RoleId, cancellationToken);

        if (role is null)
        {
            return Result.Failure(AuthorizationErrors.RoleNotFound);
        }

        if (!role.IsActive)
        {
            return Result.Failure(AuthorizationErrors.RoleInactive);
        }

        Result escalationCheck = await CheckNoEscalationAsync(
            role, command.ScopeType, command.ActingUserId, cancellationToken);

        if (escalationCheck.IsFailure)
        {
            return escalationCheck;
        }

        var scope = new GrantedScope(command.ScopeType, command.ScopeUnitId);

        if (await repository.AssignmentExistsAsync(
                command.UserId, command.RoleId, command.ScopeType, command.ScopeUnitId, cancellationToken))
        {
            return Result.Failure(AuthorizationErrors.AlreadyGranted);
        }

        Result<UserRoleAssignment> assignment = UserRoleAssignment.Grant(
            command.UserId, command.RoleId, scope, command.ActingUserId, now, command.ExpiresAt);

        if (assignment.IsFailure)
        {
            return Result.Failure(assignment.Errors);
        }

        repository.AddAssignment(assignment.Value);

        await outbox.EnqueueAsync(
            new RoleGrantedToUserEvent(
                assignment.Value.Id, command.UserId, role.Id, role.Code,
                command.ScopeType.ToString(), command.ScopeUnitId,
                command.ActingUserId, command.ExpiresAt, now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Bumped after the commit. Bumping first would invalidate every cache
        // for a change that might then roll back, which is wasteful; bumping
        // after means the new grant is visible from the next request onward.
        await versionStore.BumpAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Refuses a grant that would hand on more than the granter holds.
    /// </summary>
    private async Task<Result> CheckNoEscalationAsync(
        Role role,
        ScopeType requestedScope,
        Guid actingUserId,
        CancellationToken cancellationToken)
    {
        EffectivePermissions granterPermissions =
            await resolver.GetEffectivePermissionsAsync(actingUserId, cancellationToken);

        foreach (RolePermission rolePermission in role.Permissions)
        {
            Permission? permission =
                await repository.FindPermissionAsync(rolePermission.PermissionId, cancellationToken);

            if (permission is null)
            {
                continue;
            }

            if (!granterPermissions.Holds(permission.Name))
            {
                return Result.Failure(
                    AuthorizationErrors.CannotGrantUnheldPermission(permission.Name));
            }

            ScopeType? granterScope = granterPermissions.WidestScopeFor(permission.Name);

            if (granterScope is null || requestedScope > granterScope)
            {
                return Result.Failure(
                    AuthorizationErrors.CannotGrantWiderScope(requestedScope.ToString()));
            }
        }

        return Result.Success();
    }
}

/// <summary>Revoking a role assignment.</summary>
public sealed record RevokeRoleCommand(Guid AssignmentId, Guid ActingUserId);

/// <summary>
/// Revokes a role assignment.
/// <para>
/// Takes effect on the very next request: the version stamp is bumped, which
/// makes every cached permission set — on every instance — stale immediately.
/// </para>
/// </summary>
public sealed class RevokeRoleHandler(
    IAuthorizationRepository repository,
    IPermissionVersionStore versionStore,
    IEffectivePermissionCache cache,
    IAuthorizationOutbox outbox,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        RevokeRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        UserRoleAssignment? assignment =
            await repository.FindAssignmentAsync(command.AssignmentId, cancellationToken);

        if (assignment is null)
        {
            return Result.Failure(AuthorizationErrors.AssignmentNotFound);
        }

        // Nobody revokes their own roles either — the mirror of the grant rule.
        // Someone under investigation must not be able to tidy their own access
        // away before anyone looks.
        if (assignment.UserId == command.ActingUserId)
        {
            return Result.Failure(AuthorizationErrors.CannotGrantToSelf);
        }

        Role? role = await repository.FindRoleAsync(assignment.RoleId, cancellationToken);

        assignment.Revoke(command.ActingUserId, now);

        await outbox.EnqueueAsync(
            new RoleRevokedFromUserEvent(
                assignment.Id, assignment.UserId, assignment.RoleId,
                role?.Code ?? "(unknown)", command.ActingUserId, now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Both, and in this order. The stamp is what guarantees correctness
        // across instances; dropping the local entry just avoids one needless
        // recomputation on this one.
        await versionStore.BumpAsync(cancellationToken);
        cache.Invalidate(assignment.UserId);

        return Result.Success();
    }
}
