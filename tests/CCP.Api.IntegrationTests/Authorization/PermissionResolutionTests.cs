using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.IntegrationTests.Authorization;

/// <summary>
/// Permission resolution against a real database.
/// <para>
/// <b>Sixty-three unit tests cover evaluation and enforcement, and none of them
/// has ever executed the join</b> (debt #16). The three things they cannot
/// answer are the three that matter: whether the role → role_permissions →
/// permissions join returns what the evaluator was given in memory, whether the
/// version stamp really increments under concurrency, and whether a revocation
/// takes effect on the very next request rather than in twenty minutes.
/// </para>
/// <para>
/// That last one is the whole design. The cache is version-stamped rather than
/// time-based precisely so a revocation is immediate; if the stamp did not work,
/// nothing would fail — access would simply persist for a while after it was
/// taken away, which is the least visible and most serious failure mode
/// available to an authorization system.
/// </para>
/// </summary>
public sealed class PermissionResolutionTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    /// <summary>
    /// The join returns the permissions the role actually holds.
    /// </summary>
    [Fact]
    public async Task ResolvingAUsersPermissions_ReadsThemThroughTheJoin()
    {
        Guid userId = Guid.CreateVersion7();

        string permission = await AnyPermissionAsync();

        await GrantAsync(userId, ARoleCode(), [permission]);

        EffectivePermissions resolved = await ResolveAsync(userId);

        Assert.Contains(permission, resolved.PermissionNames, StringComparer.Ordinal);
    }

    /// <summary>
    /// A user with no assignment resolves to nothing, rather than to everything
    /// or to an error.
    /// <para>
    /// The uninteresting-looking case, and the one worth pinning: a join written
    /// with the wrong kind of outer join returns every permission in the table
    /// for a subject with no grants, and every other test here would still pass.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AUserWithNoRoleResolvesToNothing()
    {
        EffectivePermissions resolved = await ResolveAsync(Guid.CreateVersion7());

        Assert.Empty(resolved.PermissionNames);
    }

    /// <summary>
    /// A revocation takes effect on the very next request.
    /// <para>
    /// <b>This is the reason the cache carries a version stamp instead of a
    /// time-to-live.</b> With a TTL, access removed at 09:00 keeps working until
    /// the entry expires — and nothing anywhere reports that as a problem. The
    /// resolution below happens twice against the same warm process, with a
    /// revocation between, and the second must already be without it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RevokingARoleTakesEffectOnTheNextResolution()
    {
        Guid userId = Guid.CreateVersion7();
        string permission = await AnyPermissionAsync();

        Guid assignmentId = await GrantAsync(userId, ARoleCode(), [permission]);

        EffectivePermissions before = await ResolveAsync(userId);
        Assert.Contains(permission, before.PermissionNames, StringComparer.Ordinal);

        await RevokeAsync(assignmentId);

        EffectivePermissions after = await ResolveAsync(userId);

        Assert.DoesNotContain(permission, after.PermissionNames, StringComparer.Ordinal);

        // And the stamp moved, which is what made the cached set invalid rather
        // than anything the cache itself noticed.
        Assert.NotEqual(before.Version, after.Version);
    }

    /// <summary>
    /// Two concurrent bumps both count.
    /// <para>
    /// The store uses a single atomic <c>UPDATE ... SET version = version + 1</c>
    /// rather than read-modify-write, and the difference only appears under
    /// concurrency. Read-modify-write would let two simultaneous revocations
    /// produce one increment — leaving a stamp that a cache computed before
    /// either of them still matches, so one of the two revocations silently does
    /// not take effect.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ConcurrentBumpsDoNotLoseOneAnother()
    {
        // One bump first, alone. On a database where the stamp has never been
        // written, BumpAsync falls back to inserting the singleton row -- and
        // twelve tasks taking that path together would collide on the primary
        // key. That is a real behaviour worth knowing about, and it is not what
        // this test is about; racing it here would make the test flaky about
        // something other than its subject.
        await BumpAsync();

        long before = await CurrentVersionAsync();

        const int Concurrent = 12;

        // Each on its own scope, so each gets its own DbContext and its own
        // connection. Sharing one would serialise them and test nothing.
        await Task.WhenAll(Enumerable.Range(0, Concurrent).Select(async _ =>
        {
            using IServiceScope scope = factory.Services.CreateScope();

            await scope.ServiceProvider
                .GetRequiredService<IPermissionVersionStore>()
                .BumpAsync();
        }));

        long after = await CurrentVersionAsync();

        Assert.Equal(before + Concurrent, after);
    }

    /// <summary>
    /// The permission list has no duplicates.
    /// <para>
    /// Seeding runs on every start, so it must be idempotent. A second row for
    /// the same permission would not fail anything visibly — it would quietly
    /// double every join result.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SeedingDoesNotDuplicatePermissions()
    {
        await using AuthorizationDbContext context = Authorization();

        List<string> names = await context.Permissions
            .AsNoTracking()
            .Select(p => p.Name)
            .ToListAsync();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    // --- Fixtures -----------------------------------------------------------

    private AuthorizationDbContext Authorization() =>
        new(new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    /// <summary>
    /// A unique role code.
    /// <para>
    /// Version 4, not 7. The leading hex of a UUIDv7 is a millisecond timestamp,
    /// so a truncated one is mostly clock and collides between tests running in
    /// the same instant.
    /// </para>
    /// </summary>
    private static string ARoleCode() => $"r{Guid.NewGuid():N}"[..16];

    private async Task<string> AnyPermissionAsync()
    {
        await using AuthorizationDbContext context = Authorization();

        return await context.Permissions
            .AsNoTracking()
            .Where(p => p.Name == "platform.roles.manage")
            .Select(p => p.Name)
            .FirstAsync();
    }

    private async Task<EffectivePermissions> ResolveAsync(Guid userId)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IPermissionResolver>()
            .GetEffectivePermissionsAsync(userId);
    }

    private async Task<long> CurrentVersionAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IPermissionVersionStore>()
            .GetCurrentAsync();
    }

    /// <summary>
    /// Creates a role holding the named permissions and grants it, then bumps
    /// the stamp — which is what the API path does, and what a test writing rows
    /// directly must remember to do or every resolution answers from a cache
    /// computed before the grant existed.
    /// </summary>
    private async Task<Guid> GrantAsync(Guid userId, string roleCode, string[] permissions)
    {
        await using AuthorizationDbContext context = Authorization();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Role role = Role.Create(
            roleCode, "دور اختبار", "Test role",
            "Created by the permission resolution suite.", now).Value;

        foreach (string name in permissions)
        {
            Modules.Authorization.Domain.Permissions.Permission permission =
                await context.Permissions.SingleAsync(p => p.Name == name);

            role.AddPermission(permission.Id, permission.Name, now);
        }

        context.Roles.Add(role);

        UserRoleAssignment assignment = UserRoleAssignment.Grant(
            userId, role.Id, new GrantedScope(ScopeType.All, null),
            Guid.CreateVersion7(), now).Value;

        context.Assignments.Add(assignment);

        await context.SaveChangesAsync();
        await BumpAsync();

        return assignment.Id;
    }

    private async Task RevokeAsync(Guid assignmentId)
    {
        await using AuthorizationDbContext context = Authorization();

        UserRoleAssignment assignment =
            await context.Assignments.SingleAsync(a => a.Id == assignmentId);

        context.Assignments.Remove(assignment);

        await context.SaveChangesAsync();
        await BumpAsync();
    }

    private async Task BumpAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<IPermissionVersionStore>()
            .BumpAsync();
    }
}
