using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Audit.Application;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Audit.Api;

/// <summary>
/// The Audit module's registration point (ARCHITECTURE.md §7.1).
/// <para>
/// The DbContext, the repository and the partition maintenance job are
/// registered by the Infrastructure layer's own extension method, which the host
/// calls, so the Api project never references Infrastructure and the §6.1
/// dependency rule holds.
/// </para>
/// </summary>
public sealed class AuditModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "audit";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SearchAuditHandler>();
        services.AddScoped<IngestAuditHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup) => versionGroup.MapAuditEndpoints();
}
