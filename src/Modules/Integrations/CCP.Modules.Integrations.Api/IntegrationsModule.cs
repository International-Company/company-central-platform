using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Integrations.Application.Providers;
using CCP.Modules.Integrations.Application.Webhooks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Integrations.Api;

/// <summary>
/// The Integrations module's registration point (ARCHITECTURE.md §7.1).
/// <para>
/// The connector itself is registered in the infrastructure layer, because it is
/// what other modules call and it needs the resilience registry, the outbound
/// guard and the secret resolver — none of which belong to an API surface.
/// </para>
/// </summary>
public sealed class IntegrationsModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "integrations";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<RegisterProviderHandler>();
        services.AddScoped<ConfigureProviderHandler>();
        services.AddScoped<SetProviderEnabledHandler>();
        services.AddScoped<AddEndpointHandler>();
        services.AddScoped<GetProvidersHandler>();
        services.AddScoped<GetProviderHealthHandler>();
        services.AddScoped<SearchCallLogHandler>();
        services.AddScoped<ReceiveWebhookHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
        => versionGroup.MapIntegrationEndpoints();
}
