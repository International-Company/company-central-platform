using CCP.Modules.Configuration.Application;
using CCP.Modules.Configuration.Application.Abstractions;
using CCP.Modules.Configuration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Configuration.Infrastructure;

/// <summary>Registers the Configuration module's infrastructure.</summary>
public static class ConfigurationInfrastructureRegistration
{
    public static IServiceCollection AddConfigurationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<ConfigurationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history", ConfigurationDbContext.SchemaName)));

        services.AddScoped<IConfigurationRepository, ConfigurationRepository>();
        services.AddScoped<IConfigurationUnitOfWork, ConfigurationUnitOfWork>();
        services.AddScoped<IConfigurationVersionStore, ConfigurationVersionStore>();

        // Singleton: the snapshot is shared by every request in the process, and
        // the version stamp is what keeps it honest.
        services.AddSingleton<IConfigurationCache, ConfigurationCache>();

        services.AddScoped<IConfigurationReader, ConfigurationReader>();

        return services;
    }
}
