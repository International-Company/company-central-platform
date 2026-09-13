using CCP.Modules.Authorization.Application;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using CCP.Modules.Authorization.Infrastructure.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.IntegrationTests.Authorization;

/// <summary>
/// The permission list the Platform actually seeds.
/// <para>
/// <b>This suite exists because its absence cost a deployment.</b> The seeder
/// derives the permission list from endpoint metadata — correctly — but read it
/// from the <c>EndpointDataSource</c> in the container, which is a composite
/// that never sees the endpoints a minimal API maps. It resolves without
/// throwing and reports none. So the Platform declared zero permissions, gave
/// its administrator role zero permissions, and answered 403 to its own first
/// administrator on every screen, with nothing in any log to say why.
/// </para>
/// <para>
/// Every unit test passed throughout. The defect lives in the seam between the
/// composition root and the database, which is what an integration test is for.
/// </para>
/// </summary>
public sealed class PermissionSeedingTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    [Fact]
    public async Task Startup_declares_the_permissions_the_endpoints_require()
    {
        List<string> seeded = await ReadAsync(dbContext => dbContext.Permissions
            .Where(permission => permission.IsActive)
            .Select(permission => permission.Name)
            .ToListAsync());

        // Named permissions that real screens depend on, not merely a count, so
        // a failure says which capability went missing.
        Assert.Contains("platform.users.view", seeded);
        Assert.Contains("platform.roles.view", seeded);
    }

    /// <summary>
    /// Permissions checked inside a handler are catalogued too.
    /// <para>
    /// The catalogue is derived from endpoints, so one evaluated in a handler is
    /// otherwise never created and can never be held. The inspect permission
    /// was exactly that: asking about another user's access answered 403 to
    /// everybody, the first administrator included.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Permissions_checked_inside_handlers_are_catalogued()
    {
        List<string> seeded = await ReadAsync(dbContext => dbContext.Permissions
            .Where(permission => permission.IsActive)
            .Select(permission => permission.Name)
            .ToListAsync());

        Assert.Empty(HandlerPermissions.All.Except(seeded));
    }

    [Fact]
    public async Task The_administrator_role_holds_every_permission()
    {
        List<Guid> active = await ReadAsync(dbContext => dbContext.Permissions
            .Where(permission => permission.IsActive)
            .Select(permission => permission.Id)
            .ToListAsync());

        List<Guid> held = await ReadAsync(dbContext => dbContext.RolePermissions
            .Where(rolePermission => dbContext.Roles.Any(role =>
                role.Id == rolePermission.RoleId
                && role.Code == AuthorizationSeeder.AdministratorRoleCode))
            .Select(rolePermission => rolePermission.PermissionId)
            .ToListAsync());

        // The role whose entire definition is "everything". A subset would be
        // worse than none: the first administrator would reach some screens and
        // not others, which is far harder to notice.
        Assert.NotEmpty(active);
        Assert.Empty(active.Except(held));
    }

    [Fact]
    public async Task Seeding_refuses_to_run_against_a_source_with_no_endpoints()
    {
        var seeder = factory.Services.GetRequiredService<AuthorizationSeeder>();

        // The other half of the lesson. Reading no endpoints is never a correct
        // description of this Platform, and the version that treated it as one
        // deactivated nothing, granted nothing and said nothing — which is how
        // it reached production.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.SeedAsync(new EmptyRouteBuilder(factory.Services)));
    }

    /// <summary>
    /// Boots the host, then reads the database it seeded.
    /// <para>
    /// Creating the client is what builds the host, and building the host is
    /// what runs the seeder. Reading first would inspect a database nothing had
    /// written to yet.
    /// </para>
    /// </summary>
    private async Task<T> ReadAsync<T>(Func<AuthorizationDbContext, Task<T>> read)
    {
        using HttpClient client = factory.CreateClient();

        // Any request, only to be certain the pipeline is up before reading
        // what startup wrote.
        using HttpResponseMessage _ = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative));

        using IServiceScope scope = factory.Services.CreateScope();

        return await read(scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>());
    }

    /// <summary>A route builder that maps nothing, which is the failure case.</summary>
    private sealed class EmptyRouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = services;

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder()
            => new ApplicationBuilder(ServiceProvider);
    }
}
