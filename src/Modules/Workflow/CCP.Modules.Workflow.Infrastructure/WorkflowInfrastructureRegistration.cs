using CCP.Modules.Workflow.Application.Abstractions;
using CCP.Modules.Workflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Workflow.Infrastructure;

/// <summary>
/// Registers the Workflow module's infrastructure. Called by the host, the only
/// place permitted to know about an Api layer and an Infrastructure layer at
/// once (ARCHITECTURE.md §6.1).
/// </summary>
public static class WorkflowInfrastructureRegistration
{
    public static IServiceCollection AddWorkflowInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<WorkflowDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history", WorkflowDbContext.SchemaName)));

        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<IWorkflowUnitOfWork, WorkflowUnitOfWork>();
        services.AddScoped<IWorkflowOutbox, WorkflowOutbox>();

        // The seam that keeps the engine liftable: the contract is declared in
        // the Application layer, and only this line knows the answer comes from
        // Organization and Authorization.
        services.AddScoped<IAssigneeResolver, PlatformAssigneeResolver>();

        services.Configure<EscalationOptions>(
            configuration.GetSection(EscalationOptions.SectionName));

        services.AddHostedService<EscalationSweep>();

        return services;
    }
}
