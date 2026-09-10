using CCP.Modules.Organization.Contracts;

namespace CCP.Modules.Organization.UnitTests.Domain;

/// <summary>
/// Reading a materialized path back into ids.
/// <para>
/// <b>Small, and load-bearing for two other modules.</b> Documents decides who
/// may open a file from the unit chain, and Configuration decides who a feature
/// flag reaches from the same chain. Both used to split the string themselves,
/// which meant the separator was written down in three places and Organization
/// could change it while the other two carried on compiling — visible only as
/// unit-scoped rules quietly reaching nobody, which is the least detectable
/// failure an access rule has.
/// </para>
/// </summary>
public sealed class UnitPathTests
{
    private static readonly Guid Division = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Department = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Team = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void TheChainIsOrderedNearestLast()
    {
        IReadOnlyList<Guid> chain = UnitPath.ParseChain($"/{Division}/{Department}/{Team}/");

        // Nearest last, because the caller's own unit is what a "self" scope
        // resolves to and the ancestors are what a "sub-units" grant reaches.
        // Reversed, every such rule would resolve to the top of the company.
        Assert.Equal([Division, Department, Team], chain);
    }

    /// <summary>
    /// No employee record is normal rather than exceptional: service accounts
    /// and contractors sign in and have no place in the organization. An empty
    /// chain means unit rules do not reach them, which is the safe reading.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("///")]
    public void APathWithNothingInItIsAnEmptyChain(string? path)
        => Assert.Empty(UnitPath.ParseChain(path));

    /// <summary>
    /// A segment that will not parse is skipped rather than thrown over.
    /// <para>
    /// A corrupted path should cost somebody a unit-scoped rule, not the ability
    /// to use the Platform at all — and throwing here would surface as a 500
    /// from every document read and every feature check that person made.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnreadableSegmentIsSkippedRatherThanFatal()
    {
        IReadOnlyList<Guid> chain = UnitPath.ParseChain($"/{Division}/not-a-guid/{Team}/");

        Assert.Equal([Division, Team], chain);
    }

    [Fact]
    public void ASingleUnitIsAChainOfOne()
        => Assert.Equal([Division], UnitPath.ParseChain($"/{Division}/"));

    /// <summary>
    /// Whether the path carries its leading and trailing separators is not the
    /// caller's problem. Both forms appear depending on how the path was built.
    /// </summary>
    [Fact]
    public void SeparatorsAtEitherEndAreOptional()
    {
        Assert.Equal(
            UnitPath.ParseChain($"/{Division}/{Team}/"),
            UnitPath.ParseChain($"{Division}/{Team}"));
    }
}
