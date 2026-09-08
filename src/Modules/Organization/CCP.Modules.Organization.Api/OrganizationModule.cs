using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Organization.Application.Employees;
using CCP.Modules.Organization.Application.Units;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Organization.Api;

/// <summary>
/// The Organization module's registration point (ARCHITECTURE.md §7.1).
/// <para>
/// Registers only what the Api and Application layers own. Infrastructure
/// registers itself through its own extension method, called by the host, so
/// this project never references it (§6.1).
/// </para>
/// </summary>
public sealed class OrganizationModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "organization";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<CreateUnitHandler>();
        services.AddScoped<MoveUnitHandler>();
        services.AddScoped<RenameUnitHandler>();
        services.AddScoped<DeactivateUnitHandler>();
        services.AddScoped<GetUnitTreeHandler>();

        services.AddScoped<CreateEmployeeHandler>();
        services.AddScoped<TransferEmployeeHandler>();
        services.AddScoped<LinkEmployeeUserHandler>();
        services.AddScoped<SearchEmployeesHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
        => versionGroup.MapOrganizationEndpoints();
}
