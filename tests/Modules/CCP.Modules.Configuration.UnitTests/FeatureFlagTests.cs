using CCP.Modules.Configuration.Domain.Flags;

namespace CCP.Modules.Configuration.UnitTests;

/// <summary>
/// Whether a feature is on, and for whom.
/// <para>
/// Evaluation is a pure function of the flag and the caller, which is what makes
/// these tests possible without a host — and what makes the answer reproducible
/// when somebody asks "was it on for them?" after a strange report.
/// </para>
/// </summary>
public sealed class FeatureFlagTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Role = Guid.CreateVersion7();
    private static readonly Guid OtherRole = Guid.CreateVersion7();
    private static readonly Guid Division = Guid.CreateVersion7();
    private static readonly Guid Team = Guid.CreateVersion7();
    private static readonly Guid OtherUnit = Guid.CreateVersion7();

    private static FeatureFlag AFlag() =>
        FeatureFlag.Declare("platform.documents.preview", "platform", null, Now).Value;

    [Fact]
    public void ANewFlagIsOff()
    {
        // A flag created in advance of the thing it guards must not switch that
        // thing on the moment the row appears.
        Assert.False(AFlag().IsEnabled);
        Assert.False(AFlag().IsOnFor([Role], [Division]));
    }

    [Fact]
    public void AnUntargetedFlagThatIsOnReachesEverybody()
    {
        FeatureFlag flag = AFlag();
        flag.SetEnabled(true, Now);

        Assert.True(flag.IsOnFor([], []));
        Assert.True(flag.IsOnFor([Role], [Division]));
    }

    [Fact]
    public void OffIsOffHoweverItIsTargeted()
    {
        FeatureFlag flag = AFlag();
        flag.Target([Role], [Division], Now);

        // Targeting cannot switch a flag on for anybody, which is what makes the
        // master switch trustworthy at eight in the evening.
        Assert.False(flag.IsOnFor([Role], [Division]));
    }

    [Fact]
    public void ARoleTargetReachesWhoeverHoldsTheRole()
    {
        FeatureFlag flag = AFlag();
        flag.SetEnabled(true, Now);
        flag.Target([Role], [], Now);

        Assert.True(flag.IsOnFor([Role], []));
        Assert.False(flag.IsOnFor([OtherRole], []));
    }

    [Fact]
    public void AUnitTargetReachesEverythingBeneathIt()
    {
        FeatureFlag flag = AFlag();
        flag.SetEnabled(true, Now);
        flag.Target([], [Division], Now);

        // The chain is the caller's ancestry, so a flag targeted at a division
        // reaches somebody in a team below it.
        Assert.True(flag.IsOnFor([], [Division, Team]));
        Assert.False(flag.IsOnFor([], [OtherUnit]));
    }

    [Fact]
    public void EitherKindOfTargetIsEnough()
    {
        FeatureFlag flag = AFlag();
        flag.SetEnabled(true, Now);
        flag.Target([Role], [Division], Now);

        // Targets add up rather than combine. Requiring both would make "the
        // finance team and anybody with this role" impossible to express, which
        // is the ordinary case.
        Assert.True(flag.IsOnFor([Role], [OtherUnit]));
        Assert.True(flag.IsOnFor([OtherRole], [Division]));
        Assert.False(flag.IsOnFor([OtherRole], [OtherUnit]));
    }

    [Fact]
    public void TargetingNothingMeansEverybodyRatherThanNobody()
    {
        FeatureFlag flag = AFlag();
        flag.SetEnabled(true, Now);
        flag.Target([Role], [], Now);
        flag.Target([], [], Now);

        // A flag that was on and reached nobody would look broken and be
        // working, which is the worst combination available.
        Assert.True(flag.IsUntargeted);
        Assert.True(flag.IsOnFor([OtherRole], [OtherUnit]));
    }

    [Fact]
    public void AFlagMustSitInItsOwnersNamespace()
    {
        Assert.True(FeatureFlag.Declare(
            "finance.invoices.preview", "platform", null, Now).IsFailure);

        Assert.True(FeatureFlag.Declare(
            "finance.invoices.preview", "finance", null, Now).IsSuccess);
    }

    [Fact]
    public void DuplicateTargetsAreStoredOnce()
    {
        FeatureFlag flag = AFlag();
        flag.Target([Role, Role, Role], [Division, Division], Now);

        Assert.Single(flag.RoleTargets);
        Assert.Single(flag.UnitTargets);
    }
}
