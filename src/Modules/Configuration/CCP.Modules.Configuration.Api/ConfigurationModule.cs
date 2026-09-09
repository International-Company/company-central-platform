using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Configuration.Application;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Configuration.Api;

/// <summary>
/// The Configuration module's registration point (ARCHITECTURE.md §7.1).
/// </summary>
public sealed class ConfigurationModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "configuration";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<DeclareSettingHandler>();
        services.AddScoped<SetSettingHandler>();
        services.AddScoped<GetSettingsHandler>();
        services.AddScoped<GetSettingHistoryHandler>();
        services.AddScoped<DeclareFlagHandler>();
        services.AddScoped<SetFlagHandler>();
        services.AddScoped<GetFlagsHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
        => versionGroup.MapConfigurationEndpoints();
}
