using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Authorization.Application.Applications;
using CCP.Modules.Authorization.Application.Grants;
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

        services.AddScoped<GrantRoleHandler>();
        services.AddScoped<RevokeRoleHandler>();
        services.AddScoped<DeclarePermissionsHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
        => versionGroup.MapAuthorizationEndpoints();
}
