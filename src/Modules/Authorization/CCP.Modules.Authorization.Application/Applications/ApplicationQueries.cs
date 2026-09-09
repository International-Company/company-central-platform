using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Contracts.Dtos;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Domain;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Roles;

namespace CCP.Modules.Authorization.Application.Applications;

/// <summary>Turns registry types into the shapes the API publishes.</summary>
public static class ApplicationMapper
{
    public static RegisteredApplicationDto ToDto(
        RegisteredApplication application, int liveCredentials, int declaredPermissions)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new RegisteredApplicationDto(
            application.Id,
            application.Code,
            application.Name,
            application.Description,
            application.IsSystem,
            application.IsActive,
            liveCredentials,
            declaredPermissions,
            application.CreatedAt);
    }

    /// <summary>
    /// A credential without its secret, which is the only form that exists after
    /// issuance.
    /// </summary>
    public static ApplicationCredentialDto ToDto(ApplicationCredential credential, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return new ApplicationCredentialDto(
            credential.Id,
            credential.ClientId,
            credential.Label,
            credential.ExpiresAt,
            credential.RevokedAt,
            credential.LastUsedAt,
            credential.IsLive(now),
            credential.CreatedAt);
    }

    public static ApplicationRoleDto ToDto(ApplicationRoleAssignment assignment, Role role)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(role);

        return new ApplicationRoleDto(
            assignment.Id,
            assignment.RoleId,
            role.Code,
            role.NameAr,
            role.NameEn,
            assignment.ScopeType.ToString(),
            assignment.ScopeUnitId,
            assignment.GrantedAt,
            assignment.ExpiresAt,
            assignment.IsRevoked);
    }
}

/// <summary>The registry, with enough on each row to be useful.</summary>
public sealed class GetApplicationsHandler(
    IAuthorizationRepository repository,
    IApplicationRepository applications,
    IClock clock)
{
    public async Task<Result<IReadOnlyList<RegisteredApplicationDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<RegisteredApplication> registered =
            await repository.GetApplicationsAsync(cancellationToken);

        var dtos = new List<RegisteredApplicationDto>(registered.Count);

        foreach (RegisteredApplication application in registered)
        {
            // Two counts per row, and they are the two an administrator opens
            // this screen to see: whether it can still authenticate, and whether
            // it has declared anything.
            IReadOnlyList<ApplicationCredential> credentials =
                await applications.GetCredentialsAsync(application.Id, cancellationToken);

            IReadOnlyList<Domain.Permissions.Permission> permissions =
                await repository.GetPermissionsForApplicationAsync(application.Id, cancellationToken);

            dtos.Add(ApplicationMapper.ToDto(
                application,
                credentials.Count(c => c.IsLive(now)),
                permissions.Count));
        }

        return Result.Success<IReadOnlyList<RegisteredApplicationDto>>(dtos);
    }
}

/// <summary>An application's credentials, revoked ones included.</summary>
public sealed class GetCredentialsHandler(IApplicationRepository applications, IClock clock)
{
    public async Task<Result<IReadOnlyList<ApplicationCredentialDto>>> HandleAsync(
        Guid applicationId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<ApplicationCredential> credentials =
            await applications.GetCredentialsAsync(applicationId, cancellationToken);

        return Result.Success<IReadOnlyList<ApplicationCredentialDto>>(
            [.. credentials.Select(c => ApplicationMapper.ToDto(c, now))]);
    }
}

/// <summary>The roles an application holds.</summary>
public sealed class GetApplicationRolesHandler(
    IApplicationRepository applications,
    IAuthorizationRepository repository)
{
    public async Task<Result<IReadOnlyList<ApplicationRoleDto>>> HandleAsync(
        Guid applicationId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApplicationRoleAssignment> assignments =
            await applications.GetAssignmentsForApplicationAsync(applicationId, cancellationToken);

        var dtos = new List<ApplicationRoleDto>(assignments.Count);

        foreach (ApplicationRoleAssignment assignment in assignments)
        {
            Role? role = await repository.FindRoleAsync(assignment.RoleId, cancellationToken);

            if (role is not null)
            {
                dtos.Add(ApplicationMapper.ToDto(assignment, role));
            }
        }

        return Result.Success<IReadOnlyList<ApplicationRoleDto>>(dtos);
    }
}
