using System.Diagnostics.CodeAnalysis;
using CCP.Kernel.Primitives;
using CCP.Modules.Authorization.Application;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.UnitTests.Application;

/// <summary>
/// An application acting on behalf of a person.
/// <para>
/// <b>The intersection is the security property of this whole phase.</b> Taking
/// the person's rights alone would make every registered application a way to
/// act as anybody it can name; taking the application's alone would let it read
/// what the person it claims to be acting for cannot. Neither failure is
/// visible from the outside — both look like a working integration — which is
/// why they are pinned here.
/// </para>
/// </summary>
public sealed class DelegatedPermissionTests
{
    private const string Permission = "platform.employees.view";

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid ApplicationId = Guid.CreateVersion7();
    private static readonly Guid FinanceUnit = Guid.CreateVersion7();
    private static readonly Guid HrUnit = Guid.CreateVersion7();

    private const string FinancePath = "/hq/finance/";
    private const string HrPath = "/hq/hr/";

    [Fact]
    public async Task NeitherHoldingItMeansDenied()
    {
        AccessDecision decision = await Resolve(applicationGrants: [], userGrants: []);

        Assert.False(decision.IsGranted);
    }

    [Fact]
    public async Task TheApplicationHoldingItAloneIsNotEnough()
    {
        // Otherwise an application could act as anybody it can name, up to its
        // own permissions — which is impersonation with extra steps.
        AccessDecision decision = await Resolve(
            applicationGrants: [Grant(ScopeType.All)],
            userGrants: []);

        Assert.False(decision.IsGranted);
    }

    [Fact]
    public async Task ThePersonHoldingItAloneIsNotEnough()
    {
        // Otherwise a registered application inherits everything every employee
        // can do, the moment it is allowed to name one.
        AccessDecision decision = await Resolve(
            applicationGrants: [],
            userGrants: [Grant(ScopeType.All)]);

        Assert.False(decision.IsGranted);
    }

    [Fact]
    public async Task BothHoldingItCompanyWideGrantsItCompanyWide()
    {
        AccessDecision decision = await Resolve(
            applicationGrants: [Grant(ScopeType.All)],
            userGrants: [Grant(ScopeType.All)]);

        Assert.True(decision.IsGranted);
        Assert.Equal(ScopeType.All, decision.Scope);
    }

    [Fact]
    public async Task TheNarrowerScopeWins()
    {
        // The application reaches one department; the person reaches the whole
        // company. The call reaches the department.
        AccessDecision decision = await Resolve(
            applicationGrants: [Grant(ScopeType.UnitAndBelow, FinanceUnit)],
            userGrants: [Grant(ScopeType.All)]);

        Assert.True(decision.IsGranted);
        Assert.Equal(ScopeType.UnitAndBelow, decision.Scope);
        Assert.Equal([FinancePath], decision.UnitPathPrefixes);
    }

    [Fact]
    public async Task TheNarrowerScopeWinsFromTheOtherSideToo()
    {
        AccessDecision decision = await Resolve(
            applicationGrants: [Grant(ScopeType.All)],
            userGrants: [Grant(ScopeType.UnitAndBelow, HrUnit)]);

        Assert.True(decision.IsGranted);
        Assert.Equal([HrPath], decision.UnitPathPrefixes);
    }

    [Fact]
    public async Task ReachThatDoesNotOverlapGrantsNothing()
    {
        // An application scoped to finance, acting for somebody scoped to HR.
        // Both hold the permission and there is not one record they both reach.
        AccessDecision decision = await Resolve(
            applicationGrants: [Grant(ScopeType.UnitAndBelow, FinanceUnit)],
            userGrants: [Grant(ScopeType.UnitAndBelow, HrUnit)]);

        Assert.False(decision.IsGranted);
    }

    // -----------------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------------

    private static GrantRow Grant(ScopeType scope, Guid? unitId = null) =>
        new(Permission, scope, unitId);

    private static async Task<AccessDecision> Resolve(
        IReadOnlyList<GrantRow> applicationGrants,
        IReadOnlyList<GrantRow> userGrants)
    {
        var resolver = new PermissionResolver(
            new StubAuthorizationRepository(userGrants),
            new StubApplicationRepository(applicationGrants),
            new StubOrganizationReader(),
            new StubVersionStore(),
            new NoCache(),
            new FixedClock());

        return await resolver.EvaluateDelegatedAsync(ApplicationId, UserId, Permission);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    /// <summary>
    /// No caching, so each test sees exactly the grants it supplied. The cache
    /// is keyed on the subject and a version stamp, both of which are correct
    /// here — the point is to keep one test's answer out of the next.
    /// </summary>
    private sealed class NoCache : IEffectivePermissionCache
    {
        public bool TryGet(
            PermissionSubject subject,
            [NotNullWhen(true)] out EffectivePermissions? permissions)
        {
            permissions = null;

            return false;
        }

        public void Set(EffectivePermissions permissions)
        {
        }

        public void Invalidate(PermissionSubject subject)
        {
        }
    }

    private sealed class StubVersionStore : IPermissionVersionStore
    {
        public Task<long> GetCurrentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task BumpAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubOrganizationReader : IOrganizationScopeReader
    {
        public Task<string?> GetUnitPathAsync(
            Guid unitId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(
                unitId == FinanceUnit ? FinancePath : unitId == HrUnit ? HrPath : null);

        public Task<string?> GetUnitPathForUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(FinancePath);
    }

    private sealed class StubApplicationRepository(IReadOnlyList<GrantRow> grants)
        : IApplicationRepository
    {
        public Task<IReadOnlyList<GrantRow>> GetGrantsForApplicationAsync(
            Guid applicationId, DateTimeOffset now, CancellationToken cancellationToken = default)
            => Task.FromResult(grants);

        public Task<ApplicationCredential?> FindCredentialByClientIdAsync(
            string clientId, CancellationToken cancellationToken = default)
            => Task.FromResult<ApplicationCredential?>(null);

        public Task<ApplicationCredential?> FindCredentialAsync(
            Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<ApplicationCredential?>(null);

        public Task<IReadOnlyList<ApplicationCredential>> GetCredentialsAsync(
            Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ApplicationCredential>>([]);

        public void AddCredential(ApplicationCredential credential)
        {
        }

        public Task<ApplicationRoleAssignment?> FindApplicationAssignmentAsync(
            Guid assignmentId, CancellationToken cancellationToken = default)
            => Task.FromResult<ApplicationRoleAssignment?>(null);

        public Task<IReadOnlyList<ApplicationRoleAssignment>> GetAssignmentsForApplicationAsync(
            Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ApplicationRoleAssignment>>([]);

        public Task<bool> ApplicationAssignmentExistsAsync(
            Guid applicationId, Guid roleId, ScopeType scopeType, Guid? scopeUnitId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public void AddApplicationAssignment(ApplicationRoleAssignment assignment)
        {
        }
    }

    private sealed class StubAuthorizationRepository(IReadOnlyList<GrantRow> grants)
        : IAuthorizationRepository
    {
        public Task<IReadOnlyList<GrantRow>> GetGrantsForUserAsync(
            Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
            => Task.FromResult(grants);

        public Task<RegisteredApplication?> FindApplicationAsync(
            Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<RegisteredApplication?>(null);

        public Task<RegisteredApplication?> FindApplicationByCodeAsync(
            string code, CancellationToken cancellationToken = default)
            => Task.FromResult<RegisteredApplication?>(null);

        public Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RegisteredApplication>>([]);

        public void AddApplication(RegisteredApplication application)
        {
        }

        public Task<Permission?> FindPermissionAsync(
            Guid permissionId, CancellationToken cancellationToken = default)
            => Task.FromResult<Permission?>(null);

        public Task<Permission?> FindPermissionByNameAsync(
            string name, CancellationToken cancellationToken = default)
            => Task.FromResult<Permission?>(null);

        public Task<IReadOnlyList<Permission>> GetPermissionsForApplicationAsync(
            Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Permission>>([]);

        public Task<IReadOnlyList<Permission>> GetAllPermissionsAsync(
            bool includeInactive, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Permission>>([]);

        public Task<IReadOnlyList<Permission>> GetPermissionsForRoleAsync(
            Guid roleId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Permission>>([]);

        public void AddPermission(Permission permission)
        {
        }

        public Task<Role?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
            => Task.FromResult<Role?>(null);

        public Task<Role?> FindRoleByCodeAsync(
            string code, CancellationToken cancellationToken = default)
            => Task.FromResult<Role?>(null);

        public Task<IReadOnlyList<Role>> GetRolesAsync(
            bool includeInactive, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<bool> RoleCodeExistsAsync(
            string code, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public void AddRole(Role role)
        {
        }

        public Task<UserRoleAssignment?> FindAssignmentAsync(
            Guid assignmentId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserRoleAssignment?>(null);

        public Task<IReadOnlyList<UserRoleAssignment>> GetAssignmentsForUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserRoleAssignment>>([]);

        public Task<bool> AssignmentExistsAsync(
            Guid userId, Guid roleId, ScopeType scopeType, Guid? scopeUnitId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public void AddAssignment(UserRoleAssignment assignment)
        {
        }
    }
}
