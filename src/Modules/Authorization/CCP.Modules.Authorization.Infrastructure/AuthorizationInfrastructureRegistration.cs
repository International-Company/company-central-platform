using System.Security.Claims;
using CCP.Kernel.Application.Abstractions;
using CCP.Modules.Authorization.Api;
using CCP.Modules.Authorization.Application;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Roles.Events;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using CCP.Modules.Authorization.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Authorization.Infrastructure;

/// <summary>
/// Registers the Authorization module's infrastructure. Called by the host.
/// </summary>
public static class AuthorizationInfrastructureRegistration
{
    public static IServiceCollection AddAuthorizationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services.AddDbContext<AuthorizationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history", AuthorizationDbContext.SchemaName)));

        services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();
        services.AddScoped<IAuthorizationUnitOfWork, AuthorizationUnitOfWork>();
        services.AddScoped<IAuthorizationOutbox, AuthorizationOutbox>();
        services.AddScoped<IPermissionVersionStore, PermissionVersionStore>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddScoped<IOrganizationScopeReader, OrganizationScopeReader>();
        services.AddScoped<IAccessDenialRecorder, AccessDenialRecorder>();

        // The permission cache is a singleton over IMemoryCache, so it survives
        // across requests. Correctness comes from the version stamp, not from
        // the cache's lifetime.
        services.AddMemoryCache(options => options.SizeLimit = 10_000);
        services.AddSingleton<IEffectivePermissionCache, EffectivePermissionCache>();

        services.AddSingleton<AuthorizationSeeder>();

        return services;
    }
}

/// <summary>
/// Records a denied authorization decision on the outbox.
/// <para>
/// One denial is noise; a burst across many permissions from one caller is
/// someone mapping what they can reach (ARCHITECTURE.md §14.5). Recording them
/// is what makes that pattern visible to Audit from Phase 6.
/// </para>
/// <para>
/// Failures here are swallowed. A problem writing a denial record must never
/// turn a clean 403 into a 500 — that would make the enforcement path fragile in
/// exactly the situation where it matters most.
/// </para>
/// </summary>
public sealed class AccessDenialRecorder(
    IAuthorizationOutbox outbox,
    IAuthorizationUnitOfWork unitOfWork,
    IRequestContext requestContext,
    Kernel.Primitives.IClock clock,
    Microsoft.Extensions.Logging.ILogger<AccessDenialRecorder> logger) : IAccessDenialRecorder
{
    public async Task RecordAsync(Guid userId, ClaimsPrincipal principal, string permission)
    {
        try
        {
            await outbox.EnqueueAsync(new AccessDeniedEvent(
                userId,
                principal.FindFirst("username")?.Value,
                permission,
                requestContext.IpAddress,
                clock.UtcNow));

            await unitOfWork.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                logger,
                exception,
                "Failed to record an access denial for {Permission}. The denial itself still stands.",
                permission);
        }
    }
}
