using CCP.Kernel.Results;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.UnitTests.Domain;

/// <summary>
/// The rules that stop authorization amplifying itself.
/// <para>
/// Every one of these exists because without it, the ability to grant something
/// becomes the ability to grant anything.
/// </para>
/// </summary>
public sealed class GrantRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NobodyGrantsThemselvesARole()
    {
        // Without this, anyone who reaches the grant endpoint at all can
        // escalate to anything — including the permission to grant more.
        Guid userId = Guid.CreateVersion7();

        Result<UserRoleAssignment> result = UserRoleAssignment.Grant(
            userId, Guid.CreateVersion7(), GrantedScope.All, grantedBy: userId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("AUTHZ.CANNOT_GRANT_TO_SELF", result.Error.Code);
    }

    [Fact]
    public void AGrantToSomeoneElse_IsAllowed()
    {
        Result<UserRoleAssignment> result = UserRoleAssignment.Grant(
            Guid.CreateVersion7(), Guid.CreateVersion7(), GrantedScope.All,
            grantedBy: Guid.CreateVersion7(), Now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AnExpiryInThePast_IsRejected()
    {
        // A grant that is already expired is either a mistake or an attempt to
        // create an audit record of access that never worked.
        Result<UserRoleAssignment> result = UserRoleAssignment.Grant(
            Guid.CreateVersion7(), Guid.CreateVersion7(), GrantedScope.All,
            Guid.CreateVersion7(), Now, expiresAt: Now.AddMinutes(-1));

        Assert.True(result.IsFailure);
        Assert.Equal("AUTHZ.EXPIRY_IN_THE_PAST", result.Error.Code);
    }

    [Fact]
    public void AUnitOnASelfOrAllScope_IsRejected()
    {
        // A unit is meaningless for these, and accepting it would leave a value
        // in the database that reads as if it restricted something.
        Assert.True(UserRoleAssignment.Grant(
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            new GrantedScope(ScopeType.All, Guid.CreateVersion7()),
            Guid.CreateVersion7(), Now).IsFailure);

        Assert.True(UserRoleAssignment.Grant(
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            new GrantedScope(ScopeType.Self, Guid.CreateVersion7()),
            Guid.CreateVersion7(), Now).IsFailure);
    }

    [Fact]
    public void AGrantIsEffectiveUntilItExpires()
    {
        UserRoleAssignment assignment = UserRoleAssignment.Grant(
            Guid.CreateVersion7(), Guid.CreateVersion7(), GrantedScope.All,
            Guid.CreateVersion7(), Now, expiresAt: Now.AddDays(7)).Value;

        Assert.True(assignment.IsEffective(Now));
        Assert.True(assignment.IsEffective(Now.AddDays(6)));
        Assert.False(assignment.IsEffective(Now.AddDays(8)));
    }

    [Fact]
    public void ALapsedGrantNeedsNoCleanup()
    {
        // No background job removes it, which is one fewer thing that can fail
        // and leave someone holding access they should not.
        UserRoleAssignment assignment = UserRoleAssignment.Grant(
            Guid.CreateVersion7(), Guid.CreateVersion7(), GrantedScope.All,
            Guid.CreateVersion7(), Now, expiresAt: Now.AddHours(1)).Value;

        Assert.False(assignment.IsRevoked);
        Assert.False(assignment.IsEffective(Now.AddHours(2)));
    }

    [Fact]
    public void RevokingStopsAGrantImmediately()
    {
        UserRoleAssignment assignment = UserRoleAssignment.Grant(
            Guid.CreateVersion7(), Guid.CreateVersion7(), GrantedScope.All,
            Guid.CreateVersion7(), Now).Value;

        assignment.Revoke(Guid.CreateVersion7(), Now);

        Assert.True(assignment.IsRevoked);
        Assert.False(assignment.IsEffective(Now));
    }

    [Fact]
    public void RevokingIsIdempotentAndKeepsTheFirstRecord()
    {
        UserRoleAssignment assignment = UserRoleAssignment.Grant(
            Guid.CreateVersion7(), Guid.CreateVersion7(), GrantedScope.All,
            Guid.CreateVersion7(), Now).Value;

        Guid firstRevoker = Guid.CreateVersion7();

        assignment.Revoke(firstRevoker, Now);
        assignment.Revoke(Guid.CreateVersion7(), Now.AddHours(1));

        // The first revocation is what matters to an investigation.
        Assert.Equal(firstRevoker, assignment.RevokedBy);
        Assert.Equal(Now, assignment.RevokedAt);
    }
}

/// <summary>
/// Namespace ownership — the rule that keeps one application from claiming
/// another's permissions.
/// </summary>
public sealed class PermissionNamespaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("platform.users.view")]
    [InlineData("finance.invoices.approve")]
    [InlineData("hr.leave-requests.view")]
    public void AWellFormedNameParses(string value)
    {
        Result<PermissionName> result = PermissionName.Parse(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("platform")]
    [InlineData("platform.users")]
    [InlineData("platform.users.view.extra")]
    [InlineData("platform..view")]
    [InlineData("platform.users.view!")]
    [InlineData("platform.users.view users")]
    public void AMalformedNameIsRejected(string value)
    {
        Assert.True(PermissionName.Parse(value).IsFailure);
    }

    [Fact]
    public void NamesAreNormalisedToLowerCase()
    {
        // So that a check for "platform.users.view" cannot be defeated by
        // declaring "Platform.Users.View" and having the two treated as
        // different permissions.
        Assert.Equal("platform.users.view", PermissionName.Parse("Platform.Users.VIEW").Value.Value);
    }

    [Fact]
    public void AnApplicationCannotDeclareOutsideItsNamespace()
    {
        // The decisive rule. Without it, a registered business system could
        // declare platform.users.create and grant itself the Platform's own
        // administrative rights.
        RegisteredApplication finance =
            RegisteredApplication.Create("finance", "Financial System", null, Now).Value;

        Result<Permission> result = Permission.Declare(
            finance.Id, finance.Code, "platform.users.create", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("AUTHZ.PERMISSION_OUTSIDE_NAMESPACE", result.Error.Code);
    }

    [Fact]
    public void AnApplicationCanDeclareInsideItsNamespace()
    {
        RegisteredApplication finance =
            RegisteredApplication.Create("finance", "Financial System", null, Now).Value;

        Result<Permission> result = Permission.Declare(
            finance.Id, finance.Code, "finance.invoices.approve", "Approve an invoice", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("finance", result.Value.Application);
        Assert.Equal("invoices", result.Value.Resource);
        Assert.Equal("approve", result.Value.Action);
    }

    [Fact]
    public void ThePlatformNamespaceIsReserved()
    {
        // A business system claiming it could declare permissions the Platform's
        // own endpoints check for.
        Result<RegisteredApplication> result =
            RegisteredApplication.Create("platform", "Impostor", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("AUTHZ.RESERVED_APPLICATION_CODE", result.Error.Code);
    }

    [Fact]
    public void ThePlatformsOwnRegistrationUsesTheReservedNamespace()
    {
        RegisteredApplication platform = RegisteredApplication.CreatePlatform(Now);

        Assert.Equal("platform", platform.Code);
        Assert.True(platform.IsSystem);
    }

    [Fact]
    public void ThePlatformRegistrationCannotBeDeactivated()
    {
        // Deactivating it would orphan every platform.* permission and lock
        // everyone out of the Platform's own administration.
        RegisteredApplication platform = RegisteredApplication.CreatePlatform(Now);

        Assert.True(platform.Deactivate(Now).IsFailure);
    }
}

/// <summary>Role behaviour, including the system-role protection.</summary>
public sealed class RoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private static Role CreateRole() =>
        Role.Create("finance-manager", "مدير مالي", "Finance Manager", null, Now).Value;

    [Fact]
    public void AddingAPermissionIsIdempotent()
    {
        Role role = CreateRole();
        Guid permissionId = Guid.CreateVersion7();

        role.AddPermission(permissionId, "finance.invoices.approve", Now);
        role.AddPermission(permissionId, "finance.invoices.approve", Now);

        Assert.Single(role.Permissions);
    }

    [Fact]
    public void AddingAPermissionRaisesAnEvent()
    {
        // Adding a permission to a role silently widens access for every holder
        // of that role, which makes it one of the highest-impact changes anyone
        // can make. It must be auditable.
        Role role = CreateRole();

        role.AddPermission(Guid.CreateVersion7(), "finance.invoices.approve", Now);

        Assert.Single(role.DomainEvents);
    }

    [Fact]
    public void RemovingAPermissionThatIsNotThere_IsNotAnError()
    {
        Role role = CreateRole();

        Assert.True(role.RemovePermission(Guid.CreateVersion7(), "x.y.z", Now).IsSuccess);
    }

    [Fact]
    public void ASystemRoleCannotBeDeactivated()
    {
        // Removing the administrator role while it is the only thing granting
        // administrative access would lock everyone out with no way back.
        Role system = Role.CreateSystem(
            "platform-administrator", "مدير", "Administrator", "All permissions.", Now);

        Result result = system.Deactivate(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("AUTHZ.CANNOT_MODIFY_SYSTEM_ROLE", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("has.dot")]
    public void AnInvalidRoleCodeIsRejected(string code)
    {
        Assert.True(Role.Create(code, "اسم", "Name", null, Now).IsFailure);
    }

    [Fact]
    public void BothLanguageNamesAreRequired()
    {
        // Same reasoning as organizational names: an optional second language
        // becomes a permanently empty column, and the Arabic UI then shows
        // English role names.
        Assert.True(Role.Create("code-x", "", "Name", null, Now).IsFailure);
        Assert.True(Role.Create("code-x", "اسم", "", null, Now).IsFailure);
    }
}
