using CCP.Modules.Security.Application.Abstractions;
using CCP.Modules.Security.Application.Mfa;
using CCP.Modules.Security.Infrastructure.Persistence;
using CCP.Modules.Security.Infrastructure.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Security.Infrastructure;

/// <summary>
/// Registers the Security module's infrastructure.
/// <para>
/// Called by the host composition root, the only place allowed to know about an
/// Api layer and an Infrastructure layer at once (ARCHITECTURE.md §6.1).
/// </para>
/// </summary>
public static class SecurityInfrastructureRegistration
{
    public static IServiceCollection AddSecurityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services
            .AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<MfaProtectionOptions>()
            .Bind(configuration.GetSection(MfaProtectionOptions.SectionName))
            .ValidateOnStart();

        // A factory rather than AddDbContext alone, because the event recorder
        // needs a context of its own (see SecurityEventRecorder). The scoped
        // registration below is resolved through the same factory, so handlers
        // still get one context per request for everything else.
        services.AddDbContextFactory<SecurityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SecurityDbContext.SchemaName)));

        services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<SecurityDbContext>>().CreateDbContext());

        services.AddScoped<ISecurityRepository, SecurityRepository>();
        services.AddScoped<ISecurityUnitOfWork, SecurityUnitOfWork>();
        services.AddScoped<ISecurityOutbox, SecurityOutbox>();

        // Scoped, like the repository, but deliberately committing on its own.
        // See SecurityEventRecorder: an event recording a failure must survive
        // the rollback of the operation that failed.
        services.AddScoped<ISecurityEventRecorder, SecurityEventRecorder>();

        // Singletons: both are stateless and hold only configuration, and the
        // protection key should be read once rather than on every request.
        services.AddSingleton<IMfaSecretProtector, MfaSecretProtector>();
        services.AddSingleton<IRecoveryCodeGenerator, RecoveryCodeGenerator>();

        return services;
    }
}
