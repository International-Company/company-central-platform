using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.UnitTests.Domain;

/// <summary>
/// Permission evaluation.
/// <para>
/// This runs on every request in the company. A bug that grants too much is a
/// breach; one that grants too little is an outage. Both directions are tested
/// explicitly, and the defaults are chosen so that a resolution failure denies
/// rather than permits.
/// </para>
/// </summary>
public sealed class EffectivePermissionsTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private const string HqPath = "/hq/";
    private const string FinancePath = "/hq/finance/";
    private const string PayablePath = "/hq/finance/payable/";
    private const string HrPath = "/hq/hr/";

    private static EffectivePermissions Held(params HeldPermission[] permissions)
        => new(UserId, version: 1, permissions);

    private static HeldPermission Permission(string name, params ResolvedScope[] scopes)
        => new(name, scopes);

    // -----------------------------------------------------------------------
    // Denial
    // -----------------------------------------------------------------------

    [Fact]
    public void AnUnheldPermission_IsDenied()
    {
        EffectivePermissions permissions = Held(
            Permission("platform.users.view", new ResolvedScope(ScopeType.All, null)));

        AccessDecision decision = permissions.Evaluate("platform.users.create", FinancePath);

        Assert.False(decision.IsGranted);
    }

    [Fact]
    public void AnEmptySet_DeniesEverything()
    {
        AccessDecision decision = EffectivePermissions
            .None(UserId, version: 1)
            .Evaluate("platform.users.view", FinancePath);

        Assert.False(decision.IsGranted);
    }

    [Fact]
    public void PermissionNamesAreComparedExactly()
    {
        // Case-insensitive matching would let "Platform.Users.View" satisfy a
        // check for "platform.users.view", and the two would drift apart in
        // configuration. Names are normalised to lower case when parsed, so an
        // exact comparison is both correct and strict.
        EffectivePermissions permissions = Held(
            Permission("platform.users.view", new ResolvedScope(ScopeType.All, null)));

        Assert.False(permissions.Evaluate("PLATFORM.USERS.VIEW", null).IsGranted);
        Assert.True(permissions.Evaluate("platform.users.view", null).IsGranted);
    }

    // -----------------------------------------------------------------------
    // Scope resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void AllScope_GrantsWithNoFilter()
    {
        AccessDecision decision = Held(
            Permission("platform.users.view", new ResolvedScope(ScopeType.All, null)))
            .Evaluate("platform.users.view", FinancePath);

        Assert.True(decision.IsGranted);
        Assert.Equal(ScopeType.All, decision.Scope);
        Assert.Empty(decision.UnitPathPrefixes);
    }

    [Fact]
    public void SelfScope_GrantsButCoversNoUnit()
    {
        // Self is restricted by identity, not by unit. Covers() must return
        // false for any unit, or a caller with Self scope would silently gain
        // the whole department.
        AccessDecision decision = Held(
            Permission("platform.employees.view", new ResolvedScope(ScopeType.Self, null)))
            .Evaluate("platform.employees.view", FinancePath);

        Assert.True(decision.IsGranted);
        Assert.Equal(ScopeType.Self, decision.Scope);
        Assert.False(decision.Covers(FinancePath));
    }

    [Fact]
    public void AnchoredUnitScope_UsesTheAnchorNotTheCallersUnit()
    {
        // "Ahmad may see the Gaza branch", even though Ahmad works in HQ.
        // Without the anchor, delegating oversight of one part of the company
        // would mean moving the person into it.
        AccessDecision decision = Held(
            Permission("platform.employees.view", new ResolvedScope(ScopeType.Unit, HrPath)))
            .Evaluate("platform.employees.view", callerUnitPath: FinancePath);

        Assert.True(decision.IsGranted);
        Assert.Equal([HrPath], decision.UnitPathPrefixes);
        Assert.True(decision.Covers(HrPath));
        Assert.False(decision.Covers(FinancePath));
    }

    [Fact]
    public void UnanchoredUnitScope_FollowsTheCaller()
    {
        AccessDecision decision = Held(
            Permission("platform.employees.view", new ResolvedScope(ScopeType.Unit, null)))
            .Evaluate("platform.employees.view", callerUnitPath: FinancePath);

        Assert.True(decision.IsGranted);
        Assert.Equal([FinancePath], decision.UnitPathPrefixes);
    }

    [Fact]
    public void UnitScope_DoesNotReachDescendants()
    {
        // The distinction between Unit and UnitAndBelow. A department head with
        // Unit scope sees their own department, not the sections under it.
        AccessDecision decision = Held(
            Permission("platform.employees.view", new ResolvedScope(ScopeType.Unit, FinancePath)))
            .Evaluate("platform.employees.view", null);

        Assert.True(decision.Covers(FinancePath));
        Assert.False(decision.Covers(PayablePath));
    }

    [Fact]
    public void UnitAndBelowScope_ReachesDescendants()
    {
        AccessDecision decision = Held(
            Permission("platform.employees.view",
                new ResolvedScope(ScopeType.UnitAndBelow, FinancePath)))
            .Evaluate("platform.employees.view", null);

        Assert.True(decision.Covers(FinancePath));
        Assert.True(decision.Covers(PayablePath));
        Assert.False(decision.Covers(HrPath));
        Assert.False(decision.Covers(HqPath));
    }

    [Fact]
    public void UnitAndBelow_DoesNotReachUpwards()
    {
        // Scope goes down, never up. A finance manager must not see head office
        // by virtue of sitting beneath it.
        AccessDecision decision = Held(
            Permission("platform.employees.view",
                new ResolvedScope(ScopeType.UnitAndBelow, FinancePath)))
            .Evaluate("platform.employees.view", null);

        Assert.False(decision.Covers(HqPath));
    }

    // -----------------------------------------------------------------------
    // The failure mode that matters most
    // -----------------------------------------------------------------------

    [Fact]
    public void AHolderRelativeScope_GrantsNothing_WhenTheCallerHasNoUnit()
    {
        // A user with no employee record — a service account, a contractor —
        // cannot resolve "your own unit". Treating that as All would be
        // catastrophic; treating it as Self would silently widen it. It grants
        // nothing, and that default is deliberate.
        AccessDecision decision = Held(
            Permission("platform.employees.view", new ResolvedScope(ScopeType.Unit, null)))
            .Evaluate("platform.employees.view", callerUnitPath: null);

        Assert.False(decision.IsGranted);
    }

    [Fact]
    public void AnAnchoredScope_StillWorks_WhenTheCallerHasNoUnit()
    {
        // The anchor does not depend on the caller, so this must still grant.
        AccessDecision decision = Held(
            Permission("platform.employees.view",
                new ResolvedScope(ScopeType.UnitAndBelow, FinancePath)))
            .Evaluate("platform.employees.view", callerUnitPath: null);

        Assert.True(decision.IsGranted);
        Assert.True(decision.Covers(PayablePath));
    }

    // -----------------------------------------------------------------------
    // Multiple grants of the same permission
    // -----------------------------------------------------------------------

    [Fact]
    public void TheWidestScopeWins()
    {
        // Held through two roles — as a section head and as an auditor. Refusing
        // access the person has genuinely been granted elsewhere would be wrong.
        AccessDecision decision = Held(
            Permission("platform.employees.view",
                new ResolvedScope(ScopeType.Unit, PayablePath),
                new ResolvedScope(ScopeType.UnitAndBelow, FinancePath)))
            .Evaluate("platform.employees.view", null);

        Assert.Equal(ScopeType.UnitAndBelow, decision.Scope);
        Assert.True(decision.Covers(PayablePath));
    }

    [Fact]
    public void AllBeatsEverythingAndShortCircuits()
    {
        AccessDecision decision = Held(
            Permission("platform.employees.view",
                new ResolvedScope(ScopeType.Unit, PayablePath),
                new ResolvedScope(ScopeType.All, null),
                new ResolvedScope(ScopeType.Unit, HrPath)))
            .Evaluate("platform.employees.view", null);

        Assert.Equal(ScopeType.All, decision.Scope);
        Assert.Empty(decision.UnitPathPrefixes);
        Assert.True(decision.Covers("/anything/at/all/"));
    }

    [Fact]
    public void SeveralUnitGrants_AreAllReturnedAsFilters()
    {
        // Two unrelated branches. The query must filter to both, not pick one.
        AccessDecision decision = Held(
            Permission("platform.employees.view",
                new ResolvedScope(ScopeType.UnitAndBelow, FinancePath),
                new ResolvedScope(ScopeType.UnitAndBelow, HrPath)))
            .Evaluate("platform.employees.view", null);

        Assert.Equal(2, decision.UnitPathPrefixes.Count);
        Assert.True(decision.Covers(PayablePath));
        Assert.True(decision.Covers(HrPath));
    }

    // -----------------------------------------------------------------------
    // Prefix safety — the same property the org path relies on
    // -----------------------------------------------------------------------

    [Fact]
    public void PrefixMatching_CannotStrayAcrossSiblings()
    {
        // "/hq/fin/" must not cover "/hq/finance/". The trailing separator on
        // every path is what prevents it, and this asserts the property end to
        // end rather than trusting the format.
        AccessDecision decision = Held(
            Permission("platform.employees.view",
                new ResolvedScope(ScopeType.UnitAndBelow, "/hq/fin/")))
            .Evaluate("platform.employees.view", null);

        Assert.True(decision.Covers("/hq/fin/"));
        Assert.True(decision.Covers("/hq/fin/team/"));
        Assert.False(decision.Covers("/hq/finance/"));
    }

    // -----------------------------------------------------------------------
    // Helpers used by the anti-escalation check
    // -----------------------------------------------------------------------

    [Fact]
    public void Holds_IgnoresScope()
    {
        // The escalation check asks whether a granter may hand something on, not
        // what data they may see, so scope is irrelevant to it.
        EffectivePermissions permissions = Held(
            Permission("platform.users.create", new ResolvedScope(ScopeType.Self, null)));

        Assert.True(permissions.Holds("platform.users.create"));
        Assert.False(permissions.Holds("platform.users.delete"));
    }

    [Fact]
    public void WidestScopeFor_ReportsTheHighest()
    {
        EffectivePermissions permissions = Held(
            Permission("platform.users.view",
                new ResolvedScope(ScopeType.Unit, FinancePath),
                new ResolvedScope(ScopeType.UnitAndBelow, HqPath)));

        Assert.Equal(ScopeType.UnitAndBelow, permissions.WidestScopeFor("platform.users.view"));
        Assert.Null(permissions.WidestScopeFor("platform.users.delete"));
    }

    [Fact]
    public void ADeniedDecision_CoversNothing()
    {
        Assert.False(AccessDecision.Denied.Covers(FinancePath));
        Assert.False(AccessDecision.Denied.IsGranted);
    }
}
