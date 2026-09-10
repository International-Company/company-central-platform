using CCP.Kernel.Application.Configuration;
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

        // Who is asking, in the terms a flag can be aimed at. Registered here
        // rather than in the Application layer because answering it means asking
        // Authorization and Organization, which only this layer may do.
        services.AddScoped<IFeatureSubjectResolver, PlatformFeatureSubjectResolver>();

        // The kernel's settings seam, answered here. Singleton over its own
        // scope, because its callers -- the outbox relay, the job journal and
        // four background sweeps -- are singletons and could not take a scoped
        // reader. Registered after the kernel's default, which it replaces.
        services.AddSingleton<IPlatformSettings, PlatformSettingsReader>();

        services.AddScoped<PlatformSettingSeeder>();

        services.AddScoped<IConfigurationReader, ConfigurationReader>();

        return services;
    }
}
