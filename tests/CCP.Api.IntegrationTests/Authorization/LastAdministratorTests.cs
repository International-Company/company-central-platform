using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CCP.Api.IntegrationTests.Authorization;

/// <summary>
/// Which grants currently carry the ability to hand out access.
/// <para>
/// <b>The query behind the only guard that protects an unrecoverable state.</b>
/// Bootstrapping refuses to run once any user exists — correctly, because an
/// endpoint that creates an administrator on an empty database is a back door on
/// a full one. So a Platform whose last granting account is disabled, or whose
/// last granting role is emptied or switched off, cannot be recovered through
/// any interface it offers: somebody has to write a row into the production
/// database by hand.
/// </para>
/// <para>
/// Everything above this query is three lines of <c>All(...)</c>. The part that
/// can be subtly wrong is the join, and only a database can answer whether it
/// is: an assignment that is revoked, one that has expired, and one whose role
/// has been switched off all still exist as rows, and every one of them would
/// make the guard permit an operation that strands the Platform.
/// </para>
/// </summary>
public sealed class LastAdministratorTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string GrantPermission = "platform.roles.assign";

    [Fact]
    public async Task ALiveGrantOfAnActiveRoleCarriesThePermission()
    {
        (Guid userId, Guid roleId, Guid assignmentId) = await GrantAsync();

        GrantingAssignment? found = await FindAsync(assignmentId);

        Assert.NotNull(found);
        Assert.Equal(userId, found.UserId);
        Assert.Equal(roleId, found.RoleId);
    }

    /// <summary>
    /// A revoked grant carries nothing. The row survives revocation so the trail
    /// stays readable, which is exactly why a query that forgot to filter on it
    /// would look correct and count the wrong people.
    /// </summary>
    [Fact]
    public async Task ARevokedGrantCarriesNothing()
    {
        (_, _, Guid assignmentId) = await GrantAsync();

        await using (AuthorizationDbContext context = Authorization())
        {
            UserRoleAssignment assignment =
                await context.Assignments.SingleAsync(a => a.Id == assignmentId);

            assignment.Revoke(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

            await context.SaveChangesAsync();
        }

        Assert.Null(await FindAsync(assignmentId));
    }

    /// <summary>
    /// Nor does one whose term has run out. An expiry is a revocation the
    /// database performs by itself, and nothing rewrites the row when it passes.
    /// </summary>
    [Fact]
    public async Task AnExpiredGrantCarriesNothing()
    {
        (_, _, Guid assignmentId) =
            await GrantAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Null(await FindAsync(assignmentId));
    }

    /// <summary>
    /// A permission carried by a switched-off role is not carried.
    /// <para>
    /// This is the one most easily missed, and it is why switching a role off is
    /// guarded as well as emptying it: the grant is still live, the role still
    /// lists the permission, and nobody holding it can do anything at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AGrantOfAnInactiveRoleCarriesNothing()
    {
        (_, Guid roleId, Guid assignmentId) = await GrantAsync();

        await using (AuthorizationDbContext context = Authorization())
        {
            Role role = await context.Roles.SingleAsync(r => r.Id == roleId);

            role.Deactivate(DateTimeOffset.UtcNow);

            await context.SaveChangesAsync();
        }

        Assert.Null(await FindAsync(assignmentId));
    }

    /// <summary>
    /// And a role that does not carry the permission is not counted for it.
    /// <para>
    /// The dull half of the pair. A join written slightly wrong returns every
    /// assignment in the company for any permission asked about, and every other
    /// test here would still pass — while the guard would never fire again.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AGrantOfSomeOtherRoleCarriesNothing()
    {
        (_, _, Guid assignmentId) = await GrantAsync(permission: "platform.roles.view");

        Assert.Null(await FindAsync(assignmentId));
    }

    // --- Fixtures -----------------------------------------------------------

    /// <summary>
    /// Reads the database the host migrated and seeded.
    /// <para>
    /// Touching <c>Services</c> first is what makes the permission rows exist.
    /// The connection string is a plain property that starts nothing, and the
    /// permissions this suite looks up are declared by the endpoints and written
    /// by the seeder when the host starts — so a test that only read the string
    /// found an empty table and failed on a name that is certainly there.
    /// </para>
    /// </summary>
    private AuthorizationDbContext Authorization()
    {
        _ = factory.Services;

        return new AuthorizationDbContext(
            new DbContextOptionsBuilder<AuthorizationDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);
    }

    /// <summary>
    /// Looks for one assignment among everything the query returns.
    /// <para>
    /// Scoped to the assignment this test created rather than asserting on the
    /// whole set, because the suites share a database and run at the same time:
    /// an assertion about how many accounts can grant roles would be an
    /// assertion about what every other test happened to be doing.
    /// </para>
    /// </summary>
    private async Task<GrantingAssignment?> FindAsync(Guid assignmentId)
    {
        await using AuthorizationDbContext context = Authorization();

        var repository = new AuthorizationRepository(context);

        IReadOnlyList<GrantingAssignment> granting =
            await repository.GetGrantingAssignmentsAsync(GrantPermission, DateTimeOffset.UtcNow);

        return granting.FirstOrDefault(assignment => assignment.AssignmentId == assignmentId);
    }

    private async Task<(Guid UserId, Guid RoleId, Guid AssignmentId)> GrantAsync(
        string permission = GrantPermission, DateTimeOffset? expiresAt = null)
    {
        await using AuthorizationDbContext context = Authorization();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Version 4, not 7: the leading hex of a UUIDv7 is a millisecond
        // timestamp, so a truncated one is mostly clock and collides between
        // tests running in the same instant.
        Role role = Role.Create(
            $"la{Guid.NewGuid():N}"[..16], "دور اختبار", "Test role",
            "Created by the last-administrator suite.", now).Value;

        Permission carried = await context.Permissions.SingleAsync(p => p.Name == permission);

        role.AddPermission(carried.Id, carried.Name, now);

        context.Roles.Add(role);

        Guid userId = Guid.CreateVersion7();

        UserRoleAssignment assignment = UserRoleAssignment.Grant(
            userId, role.Id, new GrantedScope(ScopeType.All, null),
            Guid.CreateVersion7(), now, expiresAt).Value;

        context.Assignments.Add(assignment);

        await context.SaveChangesAsync();

        return (userId, role.Id, assignment.Id);
    }
}
