using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Security.Application.Mfa;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Security.Api;

/// <summary>
/// The Security module's registration point (ARCHITECTURE.md §7.1).
/// <para>
/// The secret protector, the recovery code generator and the DbContext are
/// registered by the Infrastructure layer's own extension method, which the host
/// calls. This class registers only what the Api and Application layers own, so
/// the Api project never references Infrastructure and the §6.1 dependency rule
/// holds.
/// </para>
/// </summary>
public sealed class SecurityModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "security";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<BeginMfaEnrolmentHandler>();
        services.AddScoped<ConfirmMfaEnrolmentHandler>();
        services.AddScoped<VerifyMfaHandler>();
        services.AddScoped<DisableMfaHandler>();

        // Enforces [RequireStepUp] wherever it appears, including on endpoints
        // in other modules. Registered here because the state it reads is this
        // module's, even though the attribute and requirement are the kernel's.
        services.AddScoped<IAuthorizationHandler, StepUpAuthorizationHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup) => versionGroup.MapSecurityEndpoints();
}
