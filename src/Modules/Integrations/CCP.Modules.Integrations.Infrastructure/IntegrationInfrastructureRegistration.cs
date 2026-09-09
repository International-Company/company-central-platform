using CCP.Modules.Integrations.Application;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Infrastructure.Outbound;
using CCP.Modules.Integrations.Infrastructure.Persistence;
using CCP.Modules.Integrations.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly.Registry;

namespace CCP.Modules.Integrations.Infrastructure;

/// <summary>
/// Registers the Integrations module's infrastructure.
/// </summary>
public static class IntegrationInfrastructureRegistration
{
    public static IServiceCollection AddIntegrationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<IntegrationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history", IntegrationDbContext.SchemaName)));

        services.AddScoped<IIntegrationRepository, IntegrationRepository>();
        services.AddScoped<IIntegrationUnitOfWork, IntegrationUnitOfWork>();
        services.AddScoped<IIntegrationOutbox, IntegrationOutbox>();

        services.Configure<IntegrationOptions>(
            configuration.GetSection(IntegrationOptions.SectionName));

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<IntegrationOptions>>().Value);

        // The allow-list check. Singleton because it holds only configuration
        // and is consulted on every outbound call in the Platform.
        services.AddSingleton<IOutboundGuard, OutboundGuard>();

        // Secrets by reference. Swapping this for a cloud secret manager is one
        // registration and no change to any provider row, which is the point of
        // naming secrets rather than storing them.
        services.AddScoped<ISecretResolver, EnvironmentSecretResolver>();

        // One registry for the whole Platform, so a circuit breaker keeps its
        // memory between calls. Built per provider and cached — a breaker
        // constructed per call would have no memory at all, which is an
        // expensive way of doing nothing.
        services.AddSingleton<ResiliencePipelineRegistry<string>>();

        // The handler chain is left alone deliberately: retry, timeout and
        // breaking are the pipeline's, and configuring them here as well would
        // give every call two of each with no way to reason about the result.
        services.AddHttpClient(HttpIntegrationConnector.HttpClientName);

        services.AddScoped<IIntegrationConnector, HttpIntegrationConnector>();

        services.AddHostedService<RetentionSweep>();

        return services;
    }
}
