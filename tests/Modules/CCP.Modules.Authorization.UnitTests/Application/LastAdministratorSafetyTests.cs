using CCP.Kernel.Primitives;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure;

namespace CCP.Modules.Authorization.UnitTests.Application;

/// <summary>
/// Whether disabling an account would leave nobody able to grant a role.
/// <para>
/// <b>Asked of the grants, not of the people, and that is the whole subtlety.</b>
/// One person can hold the permission through two roles; a count of
/// administrators cannot tell "this person is the last one" from "this grant is
/// the last one", and the two call for different answers.
/// </para>
/// <para>
/// The state it protects is unrecoverable. Bootstrapping refuses to run once any
/// user exists, so a Platform with nobody able to grant anything cannot be fixed
/// through any endpoint it has — only by writing a row into the production
/// database by hand.
/// </para>
/// </summary>
public sealed class LastAdministratorSafetyTests
{
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    [Fact]
    public async Task TheOnlyPersonWhoCanGrantIsTheLastOne()
    {
        var safety = Build(Granting(Alice), Granting(Alice));

        Assert.True(await safety.IsTheLastGrantingUserAsync(Alice));
    }

    [Fact]
    public async Task SomebodyElseHoldingItMakesThemNotTheLast()
    {
        var safety = Build(Granting(Alice), Granting(Bob));

        Assert.False(await safety.IsTheLastGrantingUserAsync(Alice));
        Assert.False(await safety.IsTheLastGrantingUserAsync(Bob));
    }

    [Fact]
    public async Task SomebodyWhoCannotGrantAtAllIsNotTheLast()
    {
        var safety = Build(Granting(Alice));

        Assert.False(await safety.IsTheLastGrantingUserAsync(Bob));
    }

    /// <summary>
    /// Nobody can grant anything already.
    /// <para>
    /// The Platform is stranded, and refusing this operation would not unstrand
    /// it — it would add a confusing error to an unrelated action and leave the
    /// real problem exactly where it was. A guard that fires after the damage is
    /// noise.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WhenNobodyCanGrantAnythingNobodyIsTheLast()
    {
        var safety = Build();

        Assert.False(await safety.IsTheLastGrantingUserAsync(Alice));
    }

    /// <summary>
    /// The permission it asks about is the one that hands out access, not
    /// whatever happens to be passed in. There is no administrator flag on a
    /// user, and this is the single definition standing in for one.
    /// </summary>
    [Fact]
    public async Task ItAsksAboutTheGrantingPermission()
    {
        var repository = new RecordingRepository([Granting(Alice)]);

        await new PlatformAdministratorSafety(repository, new FixedClock())
            .IsTheLastGrantingUserAsync(Alice);

        Assert.Equal(AdministratorSafety.GrantPermission, repository.AskedAbout);
        Assert.Equal("platform.roles.assign", repository.AskedAbout);
    }

    // --- Fixtures -----------------------------------------------------------

    private static GrantingAssignment Granting(Guid userId)
        => new(Guid.CreateVersion7(), userId, Guid.CreateVersion7());

    private static PlatformAdministratorSafety Build(params GrantingAssignment[] granting)
        => new(new RecordingRepository(granting), new FixedClock());

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Answers the one question and refuses every other, so the safety check
    /// cannot quietly start depending on something else.
    /// </summary>
    private sealed class RecordingRepository(IReadOnlyList<GrantingAssignment> granting)
        : IAuthorizationRepository
    {
        public string? AskedAbout { get; private set; }

        public Task<IReadOnlyList<GrantingAssignment>> GetGrantingAssignmentsAsync(
            string permissionName, DateTimeOffset asOf,
            CancellationToken cancellationToken = default)
        {
            AskedAbout = permissionName;

            return Task.FromResult(granting);
        }

        public Task<RegisteredApplication?> FindApplicationAsync(
            Guid applicationId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RegisteredApplication?> FindApplicationByCodeAsync(
            string code, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddApplication(RegisteredApplication application)
            => throw new NotSupportedException();

        public Task<Permission?> FindPermissionAsync(
            Guid permissionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Permission?> FindPermissionByNameAsync(
            string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Permission>> GetPermissionsForApplicationAsync(
            Guid applicationId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Permission>> GetAllPermissionsAsync(
            bool includeInactive, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Permission>> GetPermissionsForRoleAsync(
            Guid roleId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddPermission(Permission permission) => throw new NotSupportedException();

        public Task<Role?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public long VersionOf(Role role) => throw new NotSupportedException();

        public void ExpectVersion(Role role, long version) => throw new NotSupportedException();

        public Task<Role?> FindRoleByCodeAsync(
            string code, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetRolesAsync(
            bool includeInactive, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> RoleCodeExistsAsync(
            string code, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddRole(Role role) => throw new NotSupportedException();

        public Task<UserRoleAssignment?> FindAssignmentAsync(
            Guid assignmentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<UserRoleAssignment>> GetAssignmentsForUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AssignmentExistsAsync(
            Guid userId, Guid roleId, ScopeType scopeType, Guid? scopeUnitId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddAssignment(UserRoleAssignment assignment)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<GrantRow>> GetGrantsForUserAsync(
            Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
