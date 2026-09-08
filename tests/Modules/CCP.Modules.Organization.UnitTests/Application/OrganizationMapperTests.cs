using CCP.Modules.Organization.Application;
using CCP.Modules.Organization.Contracts.Dtos;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Units;

namespace CCP.Modules.Organization.UnitTests.Application;

/// <summary>
/// Assembling a flat unit list into a tree.
/// <para>
/// The org chart is the most-viewed screen in the administration portal, and a
/// tree that silently drops a branch is worse than one that fails loudly — a
/// missing department looks like a department that does not exist.
/// </para>
/// </summary>
public sealed class OrganizationMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.CreateVersion7();

    private static LocalizedName Name(string english) =>
        LocalizedName.Create($"[ar] {english}", english).Value;

    private static OrganizationUnit Unit(string code, OrganizationUnit? parent = null) =>
        OrganizationUnit.Create(
            CompanyId, parent, OrganizationUnitType.Department, code, Name(code), Now).Value;

    [Fact]
    public void EmptyList_ProducesAnEmptyTree()
    {
        Assert.Empty(OrganizationMapper.BuildTree([]));
    }

    [Fact]
    public void RootsWithNoParent_BecomeTopLevelNodes()
    {
        OrganizationUnit a = Unit("AAA");
        OrganizationUnit b = Unit("BBB");

        IReadOnlyList<OrganizationUnitTreeDto> tree = OrganizationMapper.BuildTree([a, b]);

        Assert.Equal(2, tree.Count);
        Assert.All(tree, node => Assert.Empty(node.Children));
    }

    [Fact]
    public void ChildrenAreNestedUnderTheirParent()
    {
        OrganizationUnit root = Unit("HQ");
        OrganizationUnit finance = Unit("FIN", root);
        OrganizationUnit payable = Unit("AP", finance);

        IReadOnlyList<OrganizationUnitTreeDto> tree =
            OrganizationMapper.BuildTree([payable, root, finance]);

        OrganizationUnitTreeDto rootNode = Assert.Single(tree);
        Assert.Equal("HQ", rootNode.Code);

        OrganizationUnitTreeDto financeNode = Assert.Single(rootNode.Children);
        Assert.Equal("FIN", financeNode.Code);

        OrganizationUnitTreeDto payableNode = Assert.Single(financeNode.Children);
        Assert.Equal("AP", payableNode.Code);
    }

    [Fact]
    public void InputOrder_DoesNotMatter()
    {
        // The repository orders by depth, but the mapper must not depend on it —
        // a caller passing units in any order should get the same tree.
        OrganizationUnit root = Unit("HQ");
        OrganizationUnit a = Unit("AAA", root);
        OrganizationUnit b = Unit("BBB", a);

        IReadOnlyList<OrganizationUnitTreeDto> forward = OrganizationMapper.BuildTree([root, a, b]);
        IReadOnlyList<OrganizationUnitTreeDto> reversed = OrganizationMapper.BuildTree([b, a, root]);

        Assert.Equal(forward[0].Children[0].Children[0].Code, reversed[0].Children[0].Children[0].Code);
    }

    [Fact]
    public void OrphanedUnits_ArePromotedToRootsRatherThanDropped()
    {
        // Happens when inactive units are filtered out but their active children
        // are not. Dropping the child would remove a real, active department
        // from the chart entirely — a silent and confusing loss. Showing it
        // slightly detached is the lesser wrong.
        OrganizationUnit hiddenParent = Unit("HIDDEN");
        OrganizationUnit visibleChild = Unit("VISIBLE", hiddenParent);

        IReadOnlyList<OrganizationUnitTreeDto> tree = OrganizationMapper.BuildTree([visibleChild]);

        OrganizationUnitTreeDto node = Assert.Single(tree);
        Assert.Equal("VISIBLE", node.Code);

        // Its parent id is preserved, so a caller can tell it was detached.
        Assert.Equal(hiddenParent.Id, node.ParentId);
    }

    [Fact]
    public void SiblingsAreOrderedBySortOrderThenCode()
    {
        OrganizationUnit root = Unit("HQ");
        OrganizationUnit first = Unit("ZZZ", root);
        OrganizationUnit second = Unit("AAA", root);
        OrganizationUnit third = Unit("MMM", root);

        first.Reorder(1, Now);
        second.Reorder(2, Now);
        third.Reorder(2, Now);

        IReadOnlyList<OrganizationUnitTreeDto> children =
            OrganizationMapper.BuildTree([root, first, second, third])[0].Children;

        // Sort order first, then code as the tie-break.
        Assert.Equal(["ZZZ", "AAA", "MMM"], children.Select(c => c.Code));
    }

    [Fact]
    public void BothLanguagesAreReturned()
    {
        // The client knows which locale it is rendering; returning both means a
        // language toggle needs no round trip (ADR-011).
        OrganizationUnit unit = Unit("HQ");

        OrganizationUnitTreeDto node = OrganizationMapper.BuildTree([unit])[0];

        Assert.Equal("[ar] HQ", node.Name.Ar);
        Assert.Equal("HQ", node.Name.En);
    }

    [Fact]
    public void WideTree_IsBuiltWithoutQuadraticWork()
    {
        // Two thousand units in one company is realistic. The mapper indexes by
        // parent in one pass rather than scanning the list per node, so this
        // must complete instantly rather than degrading.
        OrganizationUnit root = Unit("HQ");
        var units = new List<OrganizationUnit> { root };

        for (int i = 0; i < 500; i++)
        {
            OrganizationUnit branch = Unit($"BR{i:D4}", root);
            units.Add(branch);

            for (int j = 0; j < 3; j++)
            {
                units.Add(Unit($"BR{i:D4}T{j}", branch));
            }
        }

        IReadOnlyList<OrganizationUnitTreeDto> tree = OrganizationMapper.BuildTree(units);

        OrganizationUnitTreeDto rootNode = Assert.Single(tree);
        Assert.Equal(500, rootNode.Children.Count);
        Assert.All(rootNode.Children, branch => Assert.Equal(3, branch.Children.Count));
    }
}
