using CCP.Api.Host.Modules.Diagnostics;
using CCP.Modules.Audit.Api;
using CCP.Modules.Workflow.Api;
using CCP.Modules.Notifications.Api;
using CCP.Modules.Authorization.Api;
using CCP.Modules.Identity.Api;
using CCP.Modules.Organization.Api;
using CCP.Modules.Security.Api;
using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.Host.Modules;

/// <summary>
/// The explicit list of modules this host runs.
/// <para>
/// Registration is a hand-written list, not assembly scanning. That makes the
/// set of active modules readable in one place, makes the order deliberate, and
/// prevents a module from activating itself merely by being present (P9).
/// </para>
/// <para>
/// The eleven capability modules are added here as their phases complete.
/// Order matters only for endpoint registration, not for services: no module
/// resolves another module's services at registration time.
/// </para>
/// </summary>
public static class PlatformModules
{
    private static readonly IReadOnlyList<IPlatformModule> Modules =
    [
        new DiagnosticsModule(),
        new IdentityModule(),
        new OrganizationModule(),
        new AuthorizationModule(),
        new SecurityModule(),
        new AuditModule(),
        new WorkflowModule(),
        new NotificationsModule()

        // Phase 10: new DocumentsModule(),
        // Phase 12: new IntegrationsModule(),
        // Phase 13: new ConfigurationModule(),
        // Phase 14: new MonitoringModule(),
    ];

    /// <summary>Registers every module's services.</summary>
    public static IServiceCollection AddPlatformModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        foreach (IPlatformModule module in Modules)
        {
            module.RegisterServices(services, configuration);
        }

        return services;
    }

    /// <summary>Maps every module's endpoints onto the versioned route group.</summary>
    public static void MapPlatformModules(this IEndpointRouteBuilder versionGroup)
    {
        foreach (IModuleEndpoints module in Modules.OfType<IModuleEndpoints>())
        {
            module.MapEndpoints(versionGroup);
        }
    }

    /// <summary>The registered module names. Used by diagnostics and tests.</summary>
    public static IReadOnlyList<string> ModuleNames => [.. Modules.Select(m => m.Name)];
}
