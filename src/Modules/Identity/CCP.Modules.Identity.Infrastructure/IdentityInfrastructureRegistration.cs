using CCP.Kernel.Application.Abstractions;
using CCP.Modules.Identity.Application;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Infrastructure.Bootstrap;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Identity.Infrastructure;

/// <summary>
/// Registers the Identity module's infrastructure.
/// <para>
/// Called by the host composition root, which is the only place allowed to know
/// about both an Api layer and an Infrastructure layer at once. Keeping this out
/// of <c>IdentityModule</c> is what lets the Api project avoid referencing
/// Infrastructure, which the architecture tests enforce (ARCHITECTURE.md §6.1).
/// </para>
/// </summary>
public static class IdentityInfrastructureRegistration
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services
            .AddOptions<IdentityOptions>()
            .Bind(configuration.GetSection(IdentityOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<Argon2Options>()
            .Bind(configuration.GetSection(Argon2Options.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName)));

        // Persistence. The repository is also the module's unit of work source,
        // but the two are registered separately so a handler asks for exactly
        // the capability it needs.
        // The module's public surface: Notifications finds an address and a name,
        // and nothing else about the account.
        services.AddScoped<Contracts.IUserDirectory, UserDirectory>();

        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();
        services.AddScoped<IIdentityOutbox, IdentityOutbox>();

        // Security. Singletons: all three are stateless and hold only
        // configuration, and the signing key should be read once rather than on
        // every request.
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ISigningKeyProvider, FileSigningKeyProvider>();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<ITokenService>(sp => sp.GetRequiredService<JwtTokenService>());

        // The same instance behind both seams, deliberately. One signing key,
        // one lifetime, one set of validation parameters — a machine token and a
        // person's token differ in their claims and in nothing else, which is
        // what lets a business application validate either against the published
        // JWKS without knowing which it received.
        services.AddSingleton<Contracts.IPlatformTokenMinter>(
            sp => sp.GetRequiredService<JwtTokenService>());
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton<IJwksProvider, JwksProvider>();

        // Breached-password screening. Registered with a typed HttpClient so it
        // gets connection pooling and can later join the resilience pipeline.
        // Disabled by default; see BreachedPasswordOptions.
        services
            .AddOptions<BreachedPasswordOptions>()
            .Bind(configuration.GetSection(BreachedPasswordOptions.SectionName));

        services.AddHttpClient<IBreachedPasswordChecker, BreachedPasswordChecker>();

        // The one-time first-administrator seeder. Off unless explicitly
        // enabled, and refuses to run once any user exists.
        services
            .AddOptions<BootstrapOptions>()
            .Bind(configuration.GetSection(BootstrapOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<BootstrapAdministratorSeeder>();

        return services;
    }
}
