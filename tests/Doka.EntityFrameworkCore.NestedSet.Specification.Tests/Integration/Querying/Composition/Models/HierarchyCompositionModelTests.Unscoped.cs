namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>
    /// The direct unscoped facade composes all six APIs without requiring an artificial scope or tree wrapper.
    /// </summary>
    /// <param name="operation">The individual public query contract or rejection case.</param>
    [Theory]
    [InlineData(HierarchyCompositionOperation.Ancestors)]
    [InlineData(HierarchyCompositionOperation.AncestorsOrSelf)]
    [InlineData(HierarchyCompositionOperation.Subtrees)]
    [InlineData(HierarchyCompositionOperation.AllowedPath)]
    [InlineData(HierarchyCompositionOperation.AnyAncestor)]
    [InlineData(HierarchyCompositionOperation.NearestAncestor)]
    [InlineData(HierarchyCompositionOperation.MissingNearestOwner)]
    [InlineData(HierarchyCompositionOperation.NearestOwnerRejectsValue)]
    [InlineData(HierarchyCompositionOperation.BlockedPath)]
    public async Task DirectUnscopedFacadeComposesWithoutATreeWrapper(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var first = NextIdentity();
        var treeId = Guid.NewGuid();
        var otherTreeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<ConcreteTpcContext>(
            Engine,
            static options => new ConcreteTpcContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        foreach (var (identity, offset) in new[] { (treeId, 0), (otherTreeId, 3) })
        {
            await context.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new TpcFolderNode
            {
                Id = first + offset + depth,
                ParentId = depth == 0 ? null : first + offset + depth - 1,
                TreeId = identity,
                Left = depth + 1,
                Right = 6 - depth,
                Depth = depth,
                Name = $"Node {depth}",
            }), CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<TpcFolderNode>();
        var pairs = hierarchy.SelectSubtrees(
                anchor => anchor.TreeId == treeId && anchor.Depth == 0,
                (anchor, node, distance) => new { Anchor = anchor, Node = node })
            .Where(row => row.Node.Depth == 2);

        // Act
        var query = operation switch
        {
            HierarchyCompositionOperation.Ancestors => hierarchy.SelectAncestors(
                node => node.TreeId == treeId && node.Depth == 2,
                (node, owner, distance) => owner.Depth),
            HierarchyCompositionOperation.AncestorsOrSelf => hierarchy.SelectAncestorsOrSelf(
                node => node.TreeId == treeId && node.Depth == 2,
                (node, owner, distance) => owner.Depth),
            HierarchyCompositionOperation.Subtrees => hierarchy.SelectSubtrees(
                anchor => anchor.TreeId == treeId && anchor.Depth == 0,
                (anchor, node, distance) => node.Depth),
            HierarchyCompositionOperation.AllowedPath => hierarchy.WherePathEdges(
                    pairs, row => row.Anchor, row => row.Node,
                    (row, parent, child) => child.Depth == parent.Depth + 1)
                .Select(row => row.Node.Depth),
            HierarchyCompositionOperation.AnyAncestor => hierarchy.WhereAnyAncestorOrSelf(
                    pairs, row => row.Node, (row, owner) => owner.Depth == 0)
                .Select(row => row.Node.Depth),
            HierarchyCompositionOperation.NearestAncestor => hierarchy.WhereNearestAncestorOrSelf(
                    pairs, row => row.Node, (row, owner) => owner.Depth < 2, (row, owner) => owner.Depth == 1)
                .Select(row => row.Node.Depth),
            HierarchyCompositionOperation.MissingNearestOwner => hierarchy.WhereNearestAncestorOrSelf(
                    pairs, row => row.Node, (row, owner) => owner.Depth == 99, (row, owner) => owner.Depth == 0)
                .Select(row => row.Node.Depth),
            HierarchyCompositionOperation.NearestOwnerRejectsValue => hierarchy.WhereNearestAncestorOrSelf(
                    pairs, row => row.Node, (row, owner) => owner.Depth < 2, (row, owner) => owner.Depth == 0)
                .Select(row => row.Node.Depth),
            HierarchyCompositionOperation.BlockedPath => hierarchy.WherePathEdges(
                    pairs, row => row.Anchor, row => row.Node, (row, parent, child) => child.Depth != 1)
                .Select(row => row.Node.Depth),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var actual = await query.OrderBy(depth => depth).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }
}
