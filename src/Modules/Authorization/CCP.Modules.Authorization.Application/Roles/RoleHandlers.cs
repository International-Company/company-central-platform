using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Contracts.Dtos;
using CCP.Modules.Authorization.Domain;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.Application.Roles;

/// <summary>Defining a role.</summary>
public sealed record CreateRoleCommand(
    string Code,
    string NameAr,
    string NameEn,
    string? Description);

/// <summary>Changing a role's names and description. The code never changes.</summary>
public sealed record UpdateRoleCommand(
    Guid RoleId,
    string NameAr,
    string NameEn,
    string? Description);

/// <summary>Replacing the set of permissions a role carries.</summary>
public sealed record SetRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds,
    Guid ActingUserId,

    /// <summary>
    /// What the caller believed they were changing, or 0 to skip the check.
    /// <para>
    /// Zero is for callers with no view to be stale -- the seeder, and tests that
    /// build a role and immediately set its permissions. A screen always has one.
    /// </para>
    /// </summary>
    long ExpectedVersion = 0);

/// <summary>Turning a role off, or back on.</summary>
public sealed record SetRoleActiveCommand(Guid RoleId, bool IsActive);

/// <summary>
/// Creating a role.
/// <para>
/// A role starts empty. Its permissions are set separately, and deliberately so:
/// the two decisions have different risks — naming a bundle is administrative
/// housekeeping, filling it is handing out access — and combining them into one
/// call means the dangerous half is reviewed with the same care as the harmless
/// half.
/// </para>
/// <para>
/// Until this existed the Platform had exactly one role, created by the seeder
/// and holding every permission. Granting anyone anything therefore meant making
/// them a full administrator, which is the opposite of least privilege — and no
/// amount of scope narrowing fixes it, because scope limits <i>who</i> the
/// permissions reach, not <i>which</i> permissions they are.
/// </para>
/// </summary>
public sealed class CreateRoleHandler(
    IAuthorizationRepository repository,
    IAuthorizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    private const string ModuleName = "authorization";

    public async Task<Result<RoleDto>> HandleAsync(
        CreateRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string code = command.Code.Trim().ToLowerInvariant();

        if (await repository.RoleCodeExistsAsync(code, cancellationToken))
        {
            return Result.Failure<RoleDto>(AuthorizationErrors.RoleCodeTaken);
        }

        Result<Role> role = Role.Create(
            command.Code, command.NameAr, command.NameEn, command.Description, clock.UtcNow);

        if (role.IsFailure)
        {
            return Result.Failure<RoleDto>(role.Errors);
        }

        repository.AddRole(role.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                ModuleName,
                "role.created",
                AuditOutcome.Success,
                "role",
                role.Value.Id.ToString(),
                NewValue: $$"""{"code":"{{role.Value.Code}}"}"""),
            cancellationToken);

        return Result.Success(ToDto(role.Value));
    }

    /// <summary>
    /// The public shape, including what the role looked like when it was read.
    /// <para>
    /// The version is passed in rather than read here, because it lives in the
    /// persistence layer and this mapper has no business knowing that. Zero
    /// means "not read from a tracked entity" and is never sent back as an
    /// expectation.
    /// </para>
    /// </summary>
    internal static RoleDto ToDto(Role role, long version = 0)
        => new(role.Id, role.Code, role.NameAr, role.NameEn, role.Description,
            role.IsSystem, role.IsActive, role.Permissions.Count, version);
}

/// <summary>Renaming a role.</summary>
public sealed class UpdateRoleHandler(
    IAuthorizationRepository repository,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<RoleDto>> HandleAsync(
        UpdateRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Role? role = await repository.FindRoleAsync(command.RoleId, cancellationToken);

        if (role is null)
        {
            return Result.Failure<RoleDto>(AuthorizationErrors.RoleNotFound);
        }

        Result renamed = role.Rename(
            command.NameAr, command.NameEn, command.Description, clock.UtcNow);

        if (renamed.IsFailure)
        {
            return Result.Failure<RoleDto>(renamed.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // No permission version bump: what the role *grants* has not changed,
        // and invalidating every cached permission set for a spelling correction
        // would cost the whole Platform a round of re-resolution for nothing.
        return Result.Success(CreateRoleHandler.ToDto(role));
    }
}

/// <summary>
/// Setting what a role grants.
/// <para>
/// <b>The escalation rule applies here, not only at grant time.</b> A caller may
/// put into a role only permissions they themselves hold. Without that, someone
/// able to manage roles could define one holding everything and then have a
/// colleague grant it — the escalation still fails at the grant, but only after
/// a role exists that nobody should have been able to describe, and the person
/// who tried learns nothing about why until someone else is refused.
/// </para>
/// <para>
/// The set is replaced rather than added to. "These are the permissions" is a
/// state the caller can see and reason about; "add this one" is a sequence of
/// edits whose result nobody can predict from any single request.
/// </para>
/// </summary>
public sealed class SetRolePermissionsHandler(
    IAuthorizationRepository repository,
    IPermissionResolver resolver,
    IPermissionVersionStore versionStore,
    IAuthorizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    private const string ModuleName = "authorization";

    public async Task<Result<RoleDto>> HandleAsync(
        SetRolePermissionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Role? role = await repository.FindRoleAsync(command.RoleId, cancellationToken);

        if (role is null)
        {
            return Result.Failure<RoleDto>(AuthorizationErrors.RoleNotFound);
        }

        if (role.IsSystem)
        {
            // The administrator role holds everything by definition, maintained
            // by the seeder from the endpoints themselves. Editing it by hand
            // would be overwritten on the next startup, and in the meantime
            // could lock every administrator out of the Platform.
            return Result.Failure<RoleDto>(AuthorizationErrors.CannotModifySystemRole);
        }

        if (command.ExpectedVersion != 0)
        {
            // Told to EF before anything is changed, so the UPDATE carries it in
            // its WHERE clause. A role somebody else has already moved on
            // matches nothing and the save throws, rather than quietly
            // discarding their edit.
            repository.ExpectVersion(role, command.ExpectedVersion);
        }

        DateTimeOffset now = clock.UtcNow;

        var requested = new Dictionary<Guid, Permission>();

        foreach (Guid permissionId in command.PermissionIds.Distinct())
        {
            Permission? permission =
                await repository.FindPermissionAsync(permissionId, cancellationToken);

            if (permission is null)
            {
                return Result.Failure<RoleDto>(AuthorizationErrors.PermissionNotFound);
            }

            requested[permissionId] = permission;
        }

        EffectivePermissions held =
            await resolver.GetEffectivePermissionsAsync(command.ActingUserId, cancellationToken);

        foreach (Permission permission in requested.Values)
        {
            if (!held.PermissionNames.Contains(permission.Name))
            {
                await auditTrail.RecordAsync(
                    new AuditEntry(
                        ModuleName,
                        "role.permissions.set",
                        AuditOutcome.Denied,
                        "role",
                        role.Id.ToString(),
                        Metadata: $$"""{"permission":"{{permission.Name}}"}"""),
                    cancellationToken);

                return Result.Failure<RoleDto>(
                    AuthorizationErrors.CannotGrantUnheldPermission(permission.Name));
            }
        }

        // Removed first, so a permission that is both removed and re-added
        // cannot end up counted twice.
        foreach (RolePermission existing in role.Permissions.ToList())
        {
            if (!requested.ContainsKey(existing.PermissionId))
            {
                Permission? permission = await repository.FindPermissionAsync(
                    existing.PermissionId, cancellationToken);

                role.RemovePermission(existing.PermissionId, permission?.Name ?? string.Empty, now);
            }
        }

        foreach ((Guid permissionId, Permission permission) in requested)
        {
            role.AddPermission(permissionId, permission.Name, now);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrentChangeException)
        {
            // Somebody changed this role between the caller reading it and
            // saving. Refused rather than merged: the request is the whole
            // desired set of permissions, so applying it now would silently
            // remove whatever the other person just added, and there is no way
            // to tell from here which of the two was right.
            return Result.Failure<RoleDto>(AuthorizationErrors.RoleChangedElsewhere);
        }

        // After the commit, for the same reason grants bump afterwards: an
        // invalidation for a change that then rolled back costs the whole
        // Platform a re-resolution for nothing.
        await versionStore.BumpAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                ModuleName,
                "role.permissions.set",
                AuditOutcome.Success,
                "role",
                role.Id.ToString(),
                NewValue: $$"""{"code":"{{role.Code}}","count":{{role.Permissions.Count}}}"""),
            cancellationToken);

        return Result.Success(CreateRoleHandler.ToDto(role));
    }
}

/// <summary>
/// Turning a role off, or back on.
/// <para>
/// Deactivated rather than deleted. Assignments still reference it and audit
/// records still name it, and a role that vanishes takes the explanation of
/// somebody's past access with it. An inactive role grants nothing — the
/// resolver requires <c>role.IsActive</c> — so this is a full revocation for
/// everyone holding it, without erasing that they did.
/// </para>
/// </summary>
public sealed class SetRoleActiveHandler(
    IAuthorizationRepository repository,
    IPermissionVersionStore versionStore,
    IAuthorizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    private const string ModuleName = "authorization";

    public async Task<Result> HandleAsync(
        SetRoleActiveCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Role? role = await repository.FindRoleAsync(command.RoleId, cancellationToken);

        if (role is null)
        {
            return Result.Failure(AuthorizationErrors.RoleNotFound);
        }

        DateTimeOffset now = clock.UtcNow;

        Result changed = command.IsActive ? role.Reactivate(now) : role.Deactivate(now);

        if (changed.IsFailure)
        {
            return changed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await versionStore.BumpAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                ModuleName,
                command.IsActive ? "role.reactivated" : "role.deactivated",
                AuditOutcome.Success,
                "role",
                role.Id.ToString(),
                NewValue: $$"""{"code":"{{role.Code}}"}"""),
            cancellationToken);

        return Result.Success();
    }
}
