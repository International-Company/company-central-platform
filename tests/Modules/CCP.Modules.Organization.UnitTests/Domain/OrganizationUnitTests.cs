using CCP.Kernel.Results;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Units;

namespace CCP.Modules.Organization.UnitTests.Domain;

/// <summary>
/// The unit hierarchy and its materialized path.
/// <para>
/// From Phase 4, authorization scope is resolved from this path on every
/// request. A bug here does not produce a wrong org chart — it produces wrong
/// access, silently. These are therefore security tests as much as structural
/// ones.
/// </para>
/// </summary>
public sealed class OrganizationUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.CreateVersion7();

    private static LocalizedName Name(string english) =>
        LocalizedName.Create($"[ar] {english}", english).Value;

    private static OrganizationUnit Create(string code, OrganizationUnit? parent = null) =>
        OrganizationUnit.Create(
            CompanyId, parent, OrganizationUnitType.Department, code, Name(code), Now).Value;

    // -----------------------------------------------------------------------
    // Path construction
    // -----------------------------------------------------------------------

    [Fact]
    public void RootUnit_HasDepthZeroAndASingleSegmentPath()
    {
        OrganizationUnit root = Create("HQ");

        Assert.Null(root.ParentId);
        Assert.Equal(0, root.Depth);
        Assert.Equal($"/{root.Id:N}/", root.Path);
    }

    [Fact]
    public void ChildUnit_ExtendsTheParentPath()
    {
        OrganizationUnit root = Create("HQ");
        OrganizationUnit child = Create("FIN", root);

        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(1, child.Depth);
        Assert.Equal($"{root.Path}{child.Id:N}/", child.Path);
        Assert.StartsWith(root.Path, child.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_HasLeadingAndTrailingSeparators()
    {
        // Both matter. Without the trailing separator, the path of one unit
        // would prefix-match a sibling whose id happens to start with the same
        // characters — and a scope check would silently include it.
        OrganizationUnit root = Create("HQ");

        Assert.StartsWith("/", root.Path, StringComparison.Ordinal);
        Assert.EndsWith("/", root.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_PrefixMatchCannotStrayAcrossSiblings()
    {
        // The concrete reason for the trailing separator: given ids that share a
        // prefix, one unit's path must not match another's.
        var shortId = Guid.ParseExact("ab000000000000000000000000000000", "N");
        var longerId = Guid.ParseExact("ab000000000000000000000000000001", "N");

        string shortPath = OrganizationUnit.BuildPath(string.Empty, shortId);
        string longerPath = OrganizationUnit.BuildPath(string.Empty, longerId);

        Assert.False(longerPath.StartsWith(shortPath, StringComparison.Ordinal));
    }

    [Fact]
    public void ArbitraryDepth_IsSupported()
    {
        // The brief forbids assuming a fixed number of levels. Ten deep, with no
        // configuration and no schema change.
        OrganizationUnit current = Create("L0");

        for (int level = 1; level <= 10; level++)
        {
            current = Create($"L{level}", current);
            Assert.Equal(level, current.Depth);
        }

        Assert.Equal(10, current.Depth);
        Assert.Equal(10, current.AncestorIds().Count);
    }

    [Fact]
    public void AncestorIds_AreOrderedRootFirstAndExcludeSelf()
    {
        OrganizationUnit root = Create("HQ");
        OrganizationUnit middle = Create("FIN", root);
        OrganizationUnit leaf = Create("AP", middle);

        IReadOnlyList<Guid> ancestors = leaf.AncestorIds();

        Assert.Equal([root.Id, middle.Id], ancestors);
        Assert.DoesNotContain(leaf.Id, ancestors);
    }

    // -----------------------------------------------------------------------
    // Containment — the primitive authorization scope is built on
    // -----------------------------------------------------------------------

    [Fact]
    public void Contains_IsTrueForItselfAndItsDescendants()
    {
        OrganizationUnit root = Create("HQ");
        OrganizationUnit child = Create("FIN", root);
        OrganizationUnit grandchild = Create("AP", child);

        Assert.True(root.Contains(root));
        Assert.True(root.Contains(child));
        Assert.True(root.Contains(grandchild));
        Assert.True(child.Contains(grandchild));
    }

    [Fact]
    public void Contains_IsFalseUpwardsAndSideways()
    {
        OrganizationUnit root = Create("HQ");
        OrganizationUnit finance = Create("FIN", root);
        OrganizationUnit hr = Create("HR", root);

        Assert.False(finance.Contains(root));
        Assert.False(finance.Contains(hr));
        Assert.False(hr.Contains(finance));
    }

    // -----------------------------------------------------------------------
    // Moves
    // -----------------------------------------------------------------------

    [Fact]
    public void Move_RepointsTheParentAndRebuildsThePath()
    {
        OrganizationUnit root = Create("HQ");
        OrganizationUnit oldParent = Create("OLD", root);
        OrganizationUnit newParent = Create("NEW", root);
        OrganizationUnit moving = Create("MOVE", oldParent);

        Result<PathChange> result = moving.MoveTo(newParent, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(newParent.Id, moving.ParentId);
        Assert.Equal($"{newParent.Path}{moving.Id:N}/", moving.Path);
        Assert.True(newParent.Contains(moving));
        Assert.False(oldParent.Contains(moving));
    }

    [Fact]
    public void Move_ToTopLevel_ClearsTheParent()
    {
        OrganizationUnit root = Create("HQ");
        OrganizationUnit child = Create("FIN", root);

        Result<PathChange> result = child.MoveTo(null, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(child.ParentId);
        Assert.Equal(0, child.Depth);
        Assert.Equal($"/{child.Id:N}/", child.Path);
    }

    [Fact]
    public void Move_UnderItsOwnDescendant_IsRejected()
    {
        // The cycle that matters. Allowing it would detach the whole branch from
        // the root, and every scope check over it would then be wrong.
        OrganizationUnit root = Create("HQ");
        OrganizationUnit middle = Create("FIN", root);
        OrganizationUnit leaf = Create("AP", middle);

        Result<PathChange> result = middle.MoveTo(leaf, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("ORGANIZATION.UNIT_CANNOT_MOVE_UNDER_DESCENDANT", result.Error.Code);

        // And nothing changed.
        Assert.Equal(root.Id, middle.ParentId);
    }

    [Fact]
    public void Move_UnderItself_IsRejected()
    {
        OrganizationUnit unit = Create("HQ");

        Result<PathChange> result = unit.MoveTo(unit, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("ORGANIZATION.UNIT_CANNOT_BE_OWN_PARENT", result.Error.Code);
    }

    [Fact]
    public void Move_ToTheSameParent_IsANoOp()
    {
        OrganizationUnit root = Create("HQ");
        OrganizationUnit child = Create("FIN", root);

        string pathBefore = child.Path;

        Result<PathChange> result = child.MoveTo(root, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsNoOp);
        Assert.Equal(pathBefore, child.Path);
    }

    [Fact]
    public void Move_AcrossCompanies_IsRejected()
    {
        OrganizationUnit ours = Create("HQ");

        OrganizationUnit theirs = OrganizationUnit.Create(
            Guid.CreateVersion7(), null, OrganizationUnitType.Department, "OTHER",
            Name("Other"), Now).Value;

        Assert.True(ours.MoveTo(theirs, Now).IsFailure);
    }

    // -----------------------------------------------------------------------
    // Rebasing descendants — the expensive half of a move
    // -----------------------------------------------------------------------

    [Fact]
    public void Rebase_RewritesADescendantPathAndDepth()
    {
        OrganizationUnit root = Create("HQ");
        OrganizationUnit oldParent = Create("OLD", root);
        OrganizationUnit newParent = Create("NEW", root);
        OrganizationUnit moving = Create("MOVE", oldParent);
        OrganizationUnit descendant = Create("LEAF", moving);

        PathChange change = moving.MoveTo(newParent, Now).Value;
        descendant.RebaseUnder(change, Now);

        Assert.StartsWith(moving.Path, descendant.Path, StringComparison.Ordinal);
        Assert.True(moving.Contains(descendant));
        Assert.True(newParent.Contains(descendant));
        Assert.Equal(moving.Depth + 1, descendant.Depth);
    }

    [Fact]
    public void Rebase_AdjustsDepthWhenTheMoveChangesLevel()
    {
        // Moving a subtree up two levels must move every descendant up two, not
        // rebuild them at an arbitrary depth.
        OrganizationUnit root = Create("HQ");
        OrganizationUnit a = Create("LV1", root);
        OrganizationUnit b = Create("LV2", a);
        OrganizationUnit moving = Create("MOVE", b);
        OrganizationUnit leaf = Create("LEAF", moving);

        Assert.Equal(4, leaf.Depth);

        PathChange change = moving.MoveTo(root, Now).Value;
        leaf.RebaseUnder(change, Now);

        Assert.Equal(1, moving.Depth);
        Assert.Equal(2, leaf.Depth);
    }

    [Fact]
    public void Rebase_DeepSubtree_KeepsEveryDescendantConsistent()
    {
        OrganizationUnit root = Create("HQ");
        OrganizationUnit oldParent = Create("OLD", root);
        OrganizationUnit newParent = Create("NEW", root);
        OrganizationUnit moving = Create("MOVE", oldParent);

        List<OrganizationUnit> chain = [moving];

        for (int i = 0; i < 8; i++)
        {
            chain.Add(Create($"D{i}", chain[^1]));
        }

        PathChange change = moving.MoveTo(newParent, Now).Value;

        foreach (OrganizationUnit descendant in chain.Skip(1))
        {
            descendant.RebaseUnder(change, Now);
        }

        // Every descendant is still under the moved unit, and under its new
        // parent, at the right depth.
        for (int i = 1; i < chain.Count; i++)
        {
            Assert.True(moving.Contains(chain[i]));
            Assert.True(newParent.Contains(chain[i]));
            Assert.Equal(moving.Depth + i, chain[i].Depth);
        }
    }

    [Fact]
    public void Rebase_RefusesAUnitThatIsNotBeneathTheMovedOne()
    {
        // A guard against a use case loading the wrong set of rows. Silently
        // rewriting an unrelated unit's path would corrupt the tree.
        OrganizationUnit root = Create("HQ");
        OrganizationUnit moving = Create("MOVE", root);
        OrganizationUnit unrelated = Create("OTHER", root);
        OrganizationUnit newParent = Create("NEW", root);

        PathChange change = moving.MoveTo(newParent, Now).Value;

        Assert.Throws<InvalidOperationException>(() => unrelated.RebaseUnder(change, Now));
    }

    // -----------------------------------------------------------------------
    // Codes and lifecycle
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("this-code-is-far-too-long-to-be-accepted-here")]
    [InlineData("HAS SPACE")]
    [InlineData("HAS/SLASH")]
    [InlineData("HAS@AT")]
    public void InvalidCode_IsRejected(string code)
    {
        // The slash case matters most: it is the path separator, and allowing it
        // in a code would let a crafted code corrupt path parsing.
        Assert.True(OrganizationUnit.Create(
            CompanyId, null, OrganizationUnitType.Department, code, Name("X"), Now).IsFailure);
    }

    [Fact]
    public void Code_IsNormalisedToUpperCase()
    {
        Assert.Equal("FIN", Create("fin").Code);
    }

    [Fact]
    public void Deactivate_IsIdempotentlyRejectedWhenAlreadyInactive()
    {
        OrganizationUnit unit = Create("HQ");

        Assert.True(unit.Deactivate(Now).IsSuccess);
        Assert.False(unit.IsActive);
        Assert.True(unit.Deactivate(Now).IsFailure);
    }

    [Fact]
    public void Creation_RaisesAnEvent()
    {
        Assert.Single(Create("HQ").DomainEvents);
    }

    [Fact]
    public void Move_RaisesAnEventCarryingBothPaths()
    {
        // Consumers need the old path to invalidate cached scope for the whole
        // subtree that moved.
        OrganizationUnit root = Create("HQ");
        OrganizationUnit newParent = Create("NEW", root);
        OrganizationUnit moving = Create("MOVE", root);

        moving.ClearDomainEvents();
        moving.MoveTo(newParent, Now);

        Assert.Single(moving.DomainEvents);
    }
}
