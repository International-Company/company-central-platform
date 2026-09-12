using System.Net.Http;
using CCP.Modules.Integrations.Application;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Infrastructure.Outbound;
using CCP.Modules.Integrations.Infrastructure.Webhooks;
using CCP.Modules.Integrations.Infrastructure.Persistence;
using CCP.Modules.Integrations.Infrastructure.SecretResolution;
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

        // Every socket this client opens is opened by the guard. Singleton for
        // the same reason the guard is, and registered separately so the whole
        // policy still hangs off one seam — replacing IOutboundGuard replaces
        // what happens at the socket too, which is what a test doing so expects.
        services.AddSingleton<GuardedConnect>();

        // The handler chain is left alone deliberately: retry, timeout and
        // breaking are the pipeline's, and configuring them here as well would
        // give every call two of each with no way to reason about the result.
        //
        // The primary handler is not left alone, because connecting is where the
        // destination is finally decided. See GuardedConnect.
        services.AddHttpClient(HttpIntegrationConnector.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                ConnectCallback = sp.GetRequiredService<GuardedConnect>().ConnectAsync,

                // Without this a connection approved once is kept for the life
                // of the process, so a host that later starts resolving
                // somewhere else is never re-checked.
                PooledConnectionLifetime = GuardedConnect.PooledConnectionLifetime
            });

        services.AddScoped<IIntegrationConnector, HttpIntegrationConnector>();

        // The governed door's protocol-independent half, for channels that do
        // not speak HTTP. The email channel used it to stop being the one
        // outbound call that passed no door at all.
        services.AddScoped<Contracts.IOutboundGateway, OutboundGateway>();

        // Every Platform event is offered to the fan-out, which turns it into
        // one queued delivery per interested subscriber. Registered as an
        // observer rather than as handlers, because which events matter is
        // chosen by a business application at runtime and cannot be named here.
        services.AddScoped<CCP.Kernel.Application.Events.IIntegrationEventObserver, WebhookFanOut>();

        // And the sweep that posts them, through the same guarded socket as
        // every other outbound call.
        services.AddHostedService<WebhookDeliverySweep>();

        services.AddHostedService<RetentionSweep>();

        return services;
    }
}
