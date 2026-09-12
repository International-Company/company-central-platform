using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Organization.Application.Companies;
using CCP.Modules.Organization.Application.Employees;
using CCP.Modules.Organization.Application.Positions;
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
        services.AddScoped<GetCompanyHandler>();
        services.AddScoped<CreateCompanyHandler>();
        services.AddScoped<RenameCompanyHandler>();

        services.AddScoped<CreateUnitHandler>();
        services.AddScoped<MoveUnitHandler>();
        services.AddScoped<RenameUnitHandler>();
        services.AddScoped<DeactivateUnitHandler>();
        services.AddScoped<GetUnitTreeHandler>();

        services.AddScoped<GetPositionsHandler>();
        services.AddScoped<CreatePositionHandler>();
        services.AddScoped<RenamePositionHandler>();
        services.AddScoped<SetPositionActiveHandler>();

        services.AddScoped<CreateEmployeeHandler>();
        services.AddScoped<TransferEmployeeHandler>();
        services.AddScoped<LinkEmployeeUserHandler>();
        services.AddScoped<SearchEmployeesHandler>();

        // The typed extension bag: business applications attach their own
        // metadata to a person without a Platform schema change.
        services.AddScoped<SetEmployeeAttributeHandler>();
        services.AddScoped<RemoveEmployeeAttributeHandler>();
        services.AddScoped<GetEmployeeAttributesHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
        => versionGroup.MapOrganizationEndpoints();
}
