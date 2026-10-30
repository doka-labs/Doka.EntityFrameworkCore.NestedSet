namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionTests
{
    /// <summary>Direct endpoint reuse retains visibility, exact links, every edge rule and middle containment.</summary>
    /// <param name="distance">The number of direct edges between the selected endpoints.</param>
    /// <param name="fault">The isolated visibility, relationship, bounds or application-rule defect.</param>
    [Theory]
    [InlineData(1, "none")]
    [InlineData(2, "none")]
    [InlineData(1, "parent")]
    [InlineData(2, "parent")]
    [InlineData(2, "first-parent")]
    [InlineData(1, "depth")]
    [InlineData(2, "depth")]
    [InlineData(2, "middle-depth")]
    [InlineData(1, "first-edge")]
    [InlineData(2, "first-edge")]
    [InlineData(2, "last-edge")]
    [InlineData(2, "middle-bounds")]
    [InlineData(2, "hidden-middle")]
    [InlineData(1, "hidden-upper")]
    [InlineData(2, "hidden-lower")]
    public async Task MappedShortPathsRejectEveryBrokenDirectEdge(int distance, string fault)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<CompositionContext>(
            Engine, static options => new CompositionContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, probe.RecordMaterialization);
        var upper = new CompositionNode
        {
            Scope = scope, TreeId = tree, Id = 0, Left = 1, Right = 6,
            Visible = fault != "hidden-upper",
        };

        var lower = new CompositionNode
        {
            Scope = scope, TreeId = tree, Id = distance, ParentId = fault == "parent" ? 99 : distance - 1,
            Left = 3, Right = 4, Depth = fault == "depth" ? distance + 1 : distance,
            Visible = fault != "hidden-lower",
            EdgeOpen = fault != "last-edge" && (distance != 1 || fault != "first-edge"),
        };

        await context.AddRangeAsync([upper, lower], CancellationToken.None);

        if (fault is "parent" or "first-parent")
        {
            // WHY: Persist a real, unrelated principal so the negative query tests its link rather than
            // failing earlier at the database's required foreign-key constraint.
            await context.AddAsync(new CompositionNode
            {
                Scope = scope, TreeId = Guid.NewGuid(), Id = 99, Left = 7, Right = 8,
            }, CancellationToken.None);
        }

        if (distance == 2)
        {
            await context.AddAsync(new CompositionNode
            {
                Scope = scope, TreeId = tree, Id = 1, ParentId = fault == "first-parent" ? 99 : 0,
                Left = fault == "middle-bounds" ? 4 : 2, Right = 5, Depth = fault == "middle-depth" ? 2 : 1,
                Visible = fault != "hidden-middle", EdgeOpen = fault != "first-edge",
            }, CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(tree);
        var pairs = hierarchy.SelectSubtrees(node => node.Id == 0,
                (anchor, node, depth) => new { Anchor = anchor, Node = node })
            .Where(row => row.Node.Id == distance);

        // Act
        // WHY: Key zero is an ordinary visible principal; a missing/null FK cannot use zero as a sentinel.
        // The row-aware rule also verifies that both edge callbacks retain their actual mapped aliases.
        var actual = await hierarchy.WherePathEdges(pairs, row => row.Anchor, row => row.Node,
                (row, parent, child) => child.EdgeOpen && parent.Id >= row.Anchor.Id)
            .Select(row => row.Node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = fault == "none" ? [distance] : [];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
