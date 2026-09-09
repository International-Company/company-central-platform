using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Contracts.Dtos;
using CCP.Modules.Authorization.Domain;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.Application.Applications;

/// <summary>The module name every audit entry here carries.</summary>
internal static class ApplicationAudit
{
    public const string ModuleName = "authorization";
}

// ---------------------------------------------------------------------------
// Commands and queries
// ---------------------------------------------------------------------------

/// <summary>Registering a system that will call the Platform.</summary>
public sealed record RegisterApplicationCommand(string Code, string Name, string? Description);

/// <summary>Turning an application off, or back on.</summary>
public sealed record SetApplicationStatusCommand(Guid ApplicationId, bool IsActive);

/// <summary>Minting a client secret for an application.</summary>
public sealed record IssueCredentialCommand(
    Guid ApplicationId, string Label, DateTimeOffset? ExpiresAt);

public sealed record RevokeCredentialCommand(Guid ApplicationId, Guid CredentialId);

/// <summary>Granting an application a role, at a scope.</summary>
public sealed record GrantApplicationRoleCommand(
    Guid ApplicationId,
    Guid RoleId,
    ScopeType Scope,
    Guid? ScopeUnitId,
    DateTimeOffset? ExpiresAt,
    Guid ActingUserId);

public sealed record RevokeApplicationRoleCommand(
    Guid ApplicationId, Guid AssignmentId, Guid ActingUserId);

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

/// <summary>
/// Registers an application.
/// <para>
/// <b>There is no self-service path here</b> (ADR-012). A human holding
/// <c>platform.applications.manage</c> registers a system, and the act is
/// audited. Registration hands out a permission namespace and the ability to
/// hold roles; a system that could register itself could name its own namespace.
/// </para>
/// </summary>
public sealed class RegisterApplicationHandler(
    IAuthorizationRepository repository,
    IAuditTrail auditTrail,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<RegisteredApplicationDto>> HandleAsync(
        RegisterApplicationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        RegisteredApplication? existing =
            await repository.FindApplicationByCodeAsync(command.Code, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<RegisteredApplicationDto>(AuthorizationErrors.ApplicationCodeTaken);
        }

        Result<RegisteredApplication> created = RegisteredApplication.Create(
            command.Code, command.Name, command.Description, now);

        if (created.IsFailure)
        {
            return Result.Failure<RegisteredApplicationDto>(created.Errors);
        }

        repository.AddApplication(created.Value);

        await auditTrail.RecordAsync(
            new AuditEntry(
                ApplicationAudit.ModuleName,
                "application.registered",
                AuditOutcome.Success,
                "application",
                created.Value.Id.ToString(),
                NewValue: $$"""{"code":"{{created.Value.Code}}"}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ApplicationMapper.ToDto(created.Value, 0, 0));
    }
}

/// <summary>
/// Enables or disables an application.
/// <para>
/// Disabling is the emergency stop. It takes effect on the next permission
/// resolution rather than at the next token issue, because the grant query joins
/// on the application being active — so a token already in flight stops working
/// too, which is the behaviour somebody pulling this lever expects.
/// </para>
/// </summary>
public sealed class SetApplicationStatusHandler(
    IAuthorizationRepository repository,
    IPermissionVersionStore versionStore,
    IAuditTrail auditTrail,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        SetApplicationStatusCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        RegisteredApplication? application =
            await repository.FindApplicationAsync(command.ApplicationId, cancellationToken);

        if (application is null)
        {
            return Result.Failure(AuthorizationErrors.ApplicationNotFound);
        }

        DateTimeOffset now = clock.UtcNow;

        Result changed = command.IsActive
            ? application.Reactivate(now)
            : application.Deactivate(now);

        if (changed.IsFailure)
        {
            return changed;
        }

        await auditTrail.RecordAsync(
            new AuditEntry(
                ApplicationAudit.ModuleName,
                command.IsActive ? "application.enabled" : "application.disabled",
                AuditOutcome.Success,
                "application",
                application.Id.ToString(),
                NewValue: $$"""{"code":"{{application.Code}}"}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The permission stamp, so every cached decision about this application
        // is discarded on the next request rather than lingering for the life of
        // a cache entry.
        await versionStore.BumpAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Issues a client secret, and shows it once.
/// <para>
/// The live-credential limit is what makes rotation a two-step operation with a
/// defined end: issue the new one, deploy it, revoke the old one. Without a
/// limit an application accumulates keys nobody can account for, and "which of
/// these seven is production using?" has no answer.
/// </para>
/// </summary>
public sealed class IssueCredentialHandler(
    IAuthorizationRepository repository,
    IApplicationRepository applications,
    IApplicationSecretHasher hasher,
    IAuditTrail auditTrail,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<IssuedCredentialDto>> HandleAsync(
        IssueCredentialCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        RegisteredApplication? application =
            await repository.FindApplicationAsync(command.ApplicationId, cancellationToken);

        if (application is null)
        {
            return Result.Failure<IssuedCredentialDto>(AuthorizationErrors.ApplicationNotFound);
        }

        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<ApplicationCredential> existing =
            await applications.GetCredentialsAsync(command.ApplicationId, cancellationToken);

        if (existing.Count(c => c.IsLive(now)) >= ApplicationCredential.MaxLiveCredentials)
        {
            return Result.Failure<IssuedCredentialDto>(
                AuthorizationErrors.TooManyLiveCredentials(ApplicationCredential.MaxLiveCredentials));
        }

        (string clientId, string secret, string secretHash) = hasher.Generate();

        Result<ApplicationCredential> issued = ApplicationCredential.Issue(
            application.Id, clientId, secretHash, command.Label, now, command.ExpiresAt);

        if (issued.IsFailure)
        {
            return Result.Failure<IssuedCredentialDto>(issued.Errors);
        }

        applications.AddCredential(issued.Value);

        // The client id is recorded; the secret is not, here or anywhere. An
        // audit trail that carried it would be a second place to steal it from,
        // and audit records are kept for years.
        await auditTrail.RecordAsync(
            new AuditEntry(
                ApplicationAudit.ModuleName,
                "application.credential.issued",
                AuditOutcome.Success,
                "application",
                application.Id.ToString(),
                NewValue: $$"""{"clientId":"{{clientId}}","label":"{{issued.Value.Label}}"}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new IssuedCredentialDto(
            issued.Value.Id, clientId, secret, issued.Value.Label,
            issued.Value.ExpiresAt, issued.Value.CreatedAt));
    }
}

/// <summary>Revokes a credential. Immediately, with no grace period.</summary>
public sealed class RevokeCredentialHandler(
    IApplicationRepository applications,
    IAuditTrail auditTrail,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        RevokeCredentialCommand command,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ApplicationCredential? credential =
            await applications.FindCredentialAsync(command.CredentialId, cancellationToken);

        if (credential is null || credential.ApplicationId != command.ApplicationId)
        {
            return Result.Failure(AuthorizationErrors.CredentialNotFound);
        }

        Result revoked = credential.Revoke(actingUserId, clock.UtcNow);

        if (revoked.IsFailure)
        {
            return revoked;
        }

        await auditTrail.RecordAsync(
            new AuditEntry(
                ApplicationAudit.ModuleName,
                "application.credential.revoked",
                AuditOutcome.Success,
                "application",
                command.ApplicationId.ToString(),
                OldValue: $$"""{"clientId":"{{credential.ClientId}}","label":"{{credential.Label}}"}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Grants an application a role.
/// <para>
/// <b>The same anti-escalation rule as granting to a person</b>, and for the
/// same reason: a route to give a machine a permission you do not hold yourself
/// is a route to give yourself that permission with one extra step.
/// </para>
/// </summary>
public sealed class GrantApplicationRoleHandler(
    IAuthorizationRepository repository,
    IApplicationRepository applications,
    IPermissionResolver resolver,
    IPermissionVersionStore versionStore,
    IEffectivePermissionCache cache,
    IAuditTrail auditTrail,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        GrantApplicationRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        RegisteredApplication? application =
            await repository.FindApplicationAsync(command.ApplicationId, cancellationToken);

        if (application is null)
        {
            return Result.Failure(AuthorizationErrors.ApplicationNotFound);
        }

        Role? role = await repository.FindRoleAsync(command.RoleId, cancellationToken);

        if (role is null)
        {
            return Result.Failure(AuthorizationErrors.RoleNotFound);
        }

        if (!role.IsActive)
        {
            return Result.Failure(AuthorizationErrors.RoleInactive);
        }

        Result escalation = await CheckEscalationAsync(command, role, cancellationToken);

        if (escalation.IsFailure)
        {
            return escalation;
        }

        if (await applications.ApplicationAssignmentExistsAsync(
                command.ApplicationId, command.RoleId, command.Scope, command.ScopeUnitId,
                cancellationToken))
        {
            return Result.Failure(AuthorizationErrors.ApplicationAlreadyHoldsRole);
        }

        Result<ApplicationRoleAssignment> granted = ApplicationRoleAssignment.Grant(
            command.ApplicationId,
            command.RoleId,
            new GrantedScope(command.Scope, command.ScopeUnitId),
            command.ActingUserId,
            clock.UtcNow,
            command.ExpiresAt);

        if (granted.IsFailure)
        {
            return Result.Failure(granted.Errors);
        }

        applications.AddApplicationAssignment(granted.Value);

        await auditTrail.RecordAsync(
            new AuditEntry(
                ApplicationAudit.ModuleName,
                "application.role.granted",
                AuditOutcome.Success,
                "application",
                command.ApplicationId.ToString(),
                NewValue:
                    $$"""{"application":"{{application.Code}}","role":"{{role.Code}}","scope":"{{command.Scope}}"}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await versionStore.BumpAsync(cancellationToken);
        cache.Invalidate(PermissionSubject.ForApplication(command.ApplicationId));

        return Result.Success();
    }

    /// <summary>
    /// Nobody grants what they do not hold, at a scope wider than their own.
    /// <para>
    /// Checked against the role's whole permission set, because granting a role
    /// grants everything in it — and a check on the role rather than on its
    /// contents would let somebody hand over a role full of permissions they
    /// have never had.
    /// </para>
    /// </summary>
    private async Task<Result> CheckEscalationAsync(
        GrantApplicationRoleCommand command, Role role, CancellationToken cancellationToken)
    {
        EffectivePermissions granter =
            await resolver.GetEffectivePermissionsAsync(command.ActingUserId, cancellationToken);

        IReadOnlyList<Domain.Permissions.Permission> permissions =
            await repository.GetPermissionsForRoleAsync(role.Id, cancellationToken);

        foreach (Domain.Permissions.Permission permission in permissions)
        {
            ScopeType? held = granter.WidestScopeFor(permission.Name);

            if (held is null)
            {
                return Result.Failure(
                    AuthorizationErrors.CannotGrantUnheldPermission(permission.Name));
            }

            if (held < command.Scope)
            {
                return Result.Failure(
                    AuthorizationErrors.CannotGrantWiderScope(command.Scope.ToString()));
            }
        }

        return Result.Success();
    }
}

/// <summary>Takes a role away from an application.</summary>
public sealed class RevokeApplicationRoleHandler(
    IApplicationRepository applications,
    IPermissionVersionStore versionStore,
    IEffectivePermissionCache cache,
    IAuditTrail auditTrail,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        RevokeApplicationRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ApplicationRoleAssignment? assignment =
            await applications.FindApplicationAssignmentAsync(command.AssignmentId, cancellationToken);

        if (assignment is null || assignment.ApplicationId != command.ApplicationId)
        {
            return Result.Failure(AuthorizationErrors.AssignmentNotFound);
        }

        Result revoked = assignment.Revoke(command.ActingUserId, clock.UtcNow);

        if (revoked.IsFailure)
        {
            return revoked;
        }

        await auditTrail.RecordAsync(
            new AuditEntry(
                ApplicationAudit.ModuleName,
                "application.role.revoked",
                AuditOutcome.Success,
                "application",
                command.ApplicationId.ToString(),
                OldValue: $$"""{"roleId":"{{assignment.RoleId}}"}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await versionStore.BumpAsync(cancellationToken);
        cache.Invalidate(PermissionSubject.ForApplication(command.ApplicationId));

        return Result.Success();
    }
}
