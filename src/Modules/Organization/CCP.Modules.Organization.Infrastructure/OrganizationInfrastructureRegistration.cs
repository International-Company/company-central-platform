using CCP.Modules.Organization.Application.Abstractions;
using CCP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Organization.Infrastructure;

/// <summary>
/// Registers the Organization module's infrastructure. Called by the host, the
/// only place permitted to know about an Api layer and an Infrastructure layer
/// at once (ARCHITECTURE.md §6.1).
/// </summary>
public static class OrganizationInfrastructureRegistration
{
    public static IServiceCollection AddOrganizationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services.AddDbContext<OrganizationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history", OrganizationDbContext.SchemaName)));

        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IOrganizationUnitOfWork, OrganizationUnitOfWork>();
        services.AddScoped<IOrganizationOutbox, OrganizationOutbox>();

        // The module's public surface, through which Authorization resolves
        // scope and Workflow will resolve managers (ARCHITECTURE.md §6.2).
        services.AddScoped<Contracts.IOrganizationDirectory, OrganizationDirectory>();

        return services;
    }
}
