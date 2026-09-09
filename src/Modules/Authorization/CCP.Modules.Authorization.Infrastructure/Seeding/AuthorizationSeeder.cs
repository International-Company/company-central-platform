using CCP.Kernel.Api.Security;
using CCP.Kernel.Primitives;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Authorization.Infrastructure.Seeding;

/// <summary>
/// Registers the Platform as an application and records the permissions its own
/// endpoints declare.
/// <para>
/// <b>The permission list is derived from the endpoints themselves</b>, not from
/// a hand-maintained manifest. Every endpoint already carries
/// <see cref="RequirePermissionAttribute"/>; reading that metadata at startup
/// means the two can never drift. A separate list would be correct on the day it
/// was written and wrong within a month — someone adds an endpoint, forgets the
/// manifest, and the permission cannot be granted because it does not exist.
/// </para>
/// <para>
/// Idempotent: it runs on every startup and reconciles. Permissions no longer
/// declared by any endpoint are deactivated rather than deleted, because role
/// assignments and audit records still reference them.
/// </para>
/// </summary>
public sealed class AuthorizationSeeder(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<AuthorizationSeeder> logger)
{
    /// <summary>
    /// The role granted to the bootstrap administrator. Holds every Platform
    /// permission, and is a system role so it cannot be deactivated — removing
    /// it while it is the only thing granting administrative access would lock
    /// everyone out with no way back.
    /// </summary>
    public const string AdministratorRoleCode = "platform-administrator";

    public async Task SeedAsync(
        IEndpointRouteBuilder endpoints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The route builder, not the container. Resolving EndpointDataSource
        // from DI returns a composite that is *empty* — the endpoints a minimal
        // API maps live in the builder's own DataSources and are never
        // registered there. It resolves without throwing and answers "no
        // endpoints", so the Platform seeded zero permissions, granted the
        // administrator role zero permissions, and refused its own first
        // administrator every screen. Silent, and past every test.
        var endpointDataSource = new CompositeEndpointDataSource(endpoints.DataSources);

        using IServiceScope scope = scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        var versionStore = scope.ServiceProvider.GetRequiredService<IPermissionVersionStore>();

        DateTimeOffset now = clock.UtcNow;

        RegisteredApplication platform = await EnsurePlatformApplicationAsync(dbContext, now, cancellationToken);

        IReadOnlyList<string> declared = ReadDeclaredPermissions(endpointDataSource);

        if (declared.Count == 0)
        {
            // Never correct. Every module maps endpoints that require a
            // permission, so an empty set means the endpoints were not read —
            // not that the Platform enforces nothing. Loud, because the quiet
            // version of this shipped: nothing was logged, nothing failed, and
            // every administrative screen answered 403 with no explanation
            // anywhere.
            throw new InvalidOperationException(
                "No endpoint declares a permission. The permission list is derived from "
                + "endpoint metadata, so an empty set means the endpoints were not read "
                + "rather than that none are protected. Seeding was abandoned to avoid "
                + "deactivating every permission the Platform has.");
        }

        (int added, int deactivated) = await ReconcilePermissionsAsync(
            dbContext, platform, declared, now, cancellationToken);

        // Saved before the role is filled, for the same reason the application
        // is saved before the permissions: what comes next reads them back from
        // the database. Permissions that exist only in the change tracker are
        // invisible to a query, so the administrator role was granted nothing on
        // the run that created them — and only picked them up on the next
        // restart, which is why a fresh deployment came up unadministrable.
        await dbContext.SaveChangesAsync(cancellationToken);

        Role administrator = await EnsureAdministratorRoleAsync(dbContext, now, cancellationToken);

        int granted = await GrantAllPermissionsToAdministratorAsync(
            dbContext, administrator, now, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (added > 0 || deactivated > 0 || granted > 0)
        {
            await versionStore.BumpAsync(cancellationToken);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Authorization seeded. Permissions declared={Declared} added={Added} "
                    + "deactivated={Deactivated}; administrator role gained {Granted}.",
                    declared.Count, added, deactivated, granted);
            }
        }
    }

    /// <summary>
    /// Reads every permission declared by a mapped endpoint.
    /// <para>
    /// This is the whole point of deriving rather than listing: the set is
    /// exactly what the running application enforces.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> ReadDeclaredPermissions(EndpointDataSource endpointDataSource)
    {
        var permissions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Endpoint endpoint in endpointDataSource.Endpoints)
        {
            foreach (RequirePermissionAttribute attribute in
                     endpoint.Metadata.GetOrderedMetadata<RequirePermissionAttribute>())
            {
                permissions.Add(attribute.Permission);
            }
        }

        return [.. permissions];
    }

    private static async Task<RegisteredApplication> EnsurePlatformApplicationAsync(
        AuthorizationDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        RegisteredApplication? platform = await dbContext.Applications
            .FirstOrDefaultAsync(a => a.Code == PermissionName.PlatformNamespace, cancellationToken);

        if (platform is not null)
        {
            return platform;
        }

        platform = RegisteredApplication.CreatePlatform(now);
        dbContext.Applications.Add(platform);

        // Saved immediately: the permissions reconciled next need its id.
        await dbContext.SaveChangesAsync(cancellationToken);

        return platform;
    }

    private async Task<(int Added, int Deactivated)> ReconcilePermissionsAsync(
        AuthorizationDbContext dbContext,
        RegisteredApplication platform,
        IReadOnlyList<string> declaredNames,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<Permission> existing = await dbContext.Permissions
            .Where(p => p.ApplicationId == platform.Id)
            .ToListAsync(cancellationToken);

        var existingByName = existing.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var declaredSet = new HashSet<string>(declaredNames, StringComparer.Ordinal);

        int added = 0;

        foreach (string name in declaredNames)
        {
            if (existingByName.TryGetValue(name, out Permission? current))
            {
                current.Reactivate(now);
                continue;
            }

            Kernel.Results.Result<Permission> declared = Permission.Declare(
                platform.Id, platform.Code, name, null, now);

            if (declared.IsFailure)
            {
                // An endpoint declared something that is not a valid permission
                // name, or one outside the platform namespace. That is a coding
                // error and must be loud, not skipped — an endpoint whose
                // permission cannot be created can never be granted to anyone.
                throw new InvalidOperationException(
                    $"Endpoint metadata declares the permission '{name}', which is not valid: "
                    + string.Join("; ", declared.Errors.Select(e => e.Message)));
            }

            dbContext.Permissions.Add(declared.Value);
            added++;
        }

        int deactivated = 0;

        foreach (Permission permission in existing)
        {
            if (!declaredSet.Contains(permission.Name) && permission.IsActive)
            {
                // No endpoint requires it any more. Deactivated, not deleted:
                // roles still reference it and audit records still name it.
                permission.Deactivate(now);
                deactivated++;

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Permission {Permission} is no longer declared by any endpoint and was deactivated.",
                        permission.Name);
                }
            }
        }

        return (added, deactivated);
    }

    /// <summary>
    /// Gives the first administrator the administrator role.
    /// <para>
    /// Called by the composition root, which is the only place permitted to know
    /// that Identity created an account and that Authorization has a role for it
    /// (§6.2). Neither module may reach into the other, so neither can do this
    /// on its own — and until something did, the first administrator signed in
    /// to a Platform that refused them every screen.
    /// </para>
    /// <para>
    /// Idempotent, and it will not grant a second time. Re-running startup is
    /// normal; creating a duplicate assignment on every restart would not be.
    /// </para>
    /// </summary>
    /// <param name="onlyIfNothingGranted">
    /// When true, grants only if no role is assigned to anyone anywhere. That is
    /// the repair case: a Platform whose first administrator exists and holds
    /// nothing. Once any assignment exists the Platform is administrable and
    /// this must do nothing, or it becomes a way to hand out the administrator
    /// role on every restart.
    /// </param>
    public async Task GrantAdministratorAsync(
        Guid userId,
        bool onlyIfNothingGranted = false,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        var versionStore = scope.ServiceProvider.GetRequiredService<IPermissionVersionStore>();

        if (onlyIfNothingGranted
            && await dbContext.Assignments.AnyAsync(a => a.RevokedAt == null, cancellationToken))
        {
            return;
        }

        Role? administrator = await dbContext.Roles
            .FirstOrDefaultAsync(r => r.Code == AdministratorRoleCode, cancellationToken);

        if (administrator is null)
        {
            logger.LogError(
                "The administrator role does not exist, so {UserId} was granted nothing.",
                userId);

            return;
        }

        bool alreadyGranted = await dbContext.Assignments.AnyAsync(
            a => a.UserId == userId && a.RoleId == administrator.Id && a.RevokedAt == null,
            cancellationToken);

        if (alreadyGranted)
        {
            return;
        }

        dbContext.Assignments.Add(
            UserRoleAssignment.GrantByPlatform(userId, administrator.Id, clock.UtcNow));

        await dbContext.SaveChangesAsync(cancellationToken);

        // Bumped so the grant is visible on the very next request rather than
        // when some cache happens to lapse.
        await versionStore.BumpAsync(cancellationToken);

        logger.LogWarning(
            "The bootstrap administrator {UserId} was granted the {Role} role at All scope. "
            + "Use it to create named administrator accounts, then stop using it.",
            userId,
            AdministratorRoleCode);
    }

    private static async Task<Role> EnsureAdministratorRoleAsync(
        AuthorizationDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Role? administrator = await dbContext.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Code == AdministratorRoleCode, cancellationToken);

        if (administrator is not null)
        {
            return administrator;
        }

        administrator = Role.CreateSystem(
            AdministratorRoleCode,
            "مدير المنصة",
            "Platform Administrator",
            "Holds every Platform permission. Granted to the bootstrap administrator, "
            + "who should use it to create named administrator accounts and then stop using it.",
            now);

        dbContext.Roles.Add(administrator);
        await dbContext.SaveChangesAsync(cancellationToken);

        return administrator;
    }

    /// <summary>
    /// Keeps the administrator role holding every active Platform permission.
    /// <para>
    /// Re-run on every startup so a newly added endpoint's permission is
    /// immediately administrable. Without this, adding an endpoint would leave
    /// its permission ungranted to anyone — including the administrator — and
    /// nobody could grant it, because granting requires holding it.
    /// </para>
    /// </summary>
    private static async Task<int> GrantAllPermissionsToAdministratorAsync(
        AuthorizationDbContext dbContext,
        Role administrator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<Permission> platformPermissions = await dbContext.Permissions
            .Where(p => p.Application == PermissionName.PlatformNamespace && p.IsActive)
            .ToListAsync(cancellationToken);

        var held = administrator.Permissions.Select(p => p.PermissionId).ToHashSet();

        int granted = 0;

        foreach (Permission permission in platformPermissions)
        {
            if (held.Contains(permission.Id))
            {
                continue;
            }

            administrator.AddPermission(permission.Id, permission.Name, now);
            granted++;
        }

        // Seeding raises no integration events: it runs before anything is
        // listening, and an audit trail full of "the seeder granted a permission
        // to the administrator role" on every restart would be noise.
        administrator.ClearDomainEvents();

        return granted;
    }
}
