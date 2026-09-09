using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Workflow.Application.Definitions;
using CCP.Modules.Workflow.Application.Engine;
using CCP.Modules.Workflow.Application.Instances;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Workflow.Api;

/// <summary>
/// The Workflow module's registration point (ARCHITECTURE.md §7.1).
/// <para>
/// The engine is registered here rather than with the infrastructure because it
/// holds none: it composes a repository and an assignee resolver, both of which
/// arrive as contracts, and it is the one piece another product could take
/// unchanged.
/// </para>
/// </summary>
public sealed class WorkflowModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "workflow";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<WorkflowEngine>();

        services.AddScoped<RegisterDefinitionHandler>();
        services.AddScoped<RetireDefinitionHandler>();
        services.AddScoped<GetDefinitionsHandler>();

        services.AddScoped<StartInstanceHandler>();
        services.AddScoped<ActOnTaskHandler>();
        services.AddScoped<CancelInstanceHandler>();

        services.AddScoped<GetMyTasksHandler>();
        services.AddScoped<GetInstanceHandler>();
        services.AddScoped<SearchInstancesHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
        => versionGroup.MapWorkflowEndpoints();
}
