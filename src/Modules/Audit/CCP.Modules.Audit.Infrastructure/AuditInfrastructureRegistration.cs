using CCP.Modules.Audit.Application;
using CCP.Modules.Audit.Application.Abstractions;
using CCP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Audit.Infrastructure;

/// <summary>
/// Registers the Audit module's infrastructure.
/// <para>
/// Called by the host composition root, the only place permitted to know about
/// an Api layer and an Infrastructure layer at once (ARCHITECTURE.md §6.1).
/// </para>
/// </summary>
public static class AuditInfrastructureRegistration
{
    public static IServiceCollection AddAuditInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services
            .AddOptions<AuditOptions>()
            .Bind(configuration.GetSection(AuditOptions.SectionName))
            .ValidateOnStart();

        // A factory only. Every audit write commits on its own, so nothing here
        // wants a context shared with the request that triggered it - sharing
        // one is precisely how an audit row gets rolled back with the operation
        // it was recording.
        services.AddDbContextFactory<AuditDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.SchemaName)));

        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IAuditRecorder, AuditRecorder>();

        services.AddHostedService<AuditPartitionMaintenance>();

        return services;
    }
}
