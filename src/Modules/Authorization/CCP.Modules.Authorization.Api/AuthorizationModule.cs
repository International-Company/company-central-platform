using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Authorization.Application.Applications;
using CCP.Modules.Authorization.Application.Grants;
using CCP.Modules.Authorization.Application.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Authorization.Api;

/// <summary>
/// The Authorization module's registration point.
/// <para>
/// This is also where enforcement is switched on: the policy provider and the
/// permission handler registered here are what make every
/// <c>[RequirePermission]</c> across the Platform real. Before Phase 4 those
/// attributes declared intent and nothing evaluated them.
/// </para>
/// </summary>
public sealed class AuthorizationModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "authorization";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Turns a "permission:x" policy name into a requirement, on demand.
        // Registered as a singleton because it is stateless and caches the
        // policies it builds.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        // Scoped, because it resolves permissions through per-request services.
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddScoped<CreateRoleHandler>();
        services.AddScoped<UpdateRoleHandler>();
        services.AddScoped<SetRolePermissionsHandler>();
        services.AddScoped<SetRoleActiveHandler>();
        services.AddScoped<GrantRoleHandler>();
        services.AddScoped<RevokeRoleHandler>();
        services.AddScoped<DeclarePermissionsHandler>();

        // The application registry and the door machines come in through.
        services.AddScoped<RegisterApplicationHandler>();
        services.AddScoped<SetApplicationStatusHandler>();
        services.AddScoped<IssueCredentialHandler>();
        services.AddScoped<RevokeCredentialHandler>();
        services.AddScoped<GrantApplicationRoleHandler>();
        services.AddScoped<RevokeApplicationRoleHandler>();
        services.AddScoped<GetApplicationsHandler>();
        services.AddScoped<GetCredentialsHandler>();
        services.AddScoped<GetApplicationRolesHandler>();
        services.AddScoped<MachineTokenHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
    {
        versionGroup.MapAuthorizationEndpoints();
        versionGroup.MapApplicationEndpoints();
    }
}
