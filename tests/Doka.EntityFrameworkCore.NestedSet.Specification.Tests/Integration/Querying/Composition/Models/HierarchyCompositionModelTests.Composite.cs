namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Composite EF keys and equal scope-local NodeKeys cannot leak rows through any composer.</summary>
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
    public async Task CompositePrimaryKeysKeepEqualNodeKeysAndBoundsIsolatedByScope(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var scope = NextIdentity();
        var otherScope = NextIdentity();
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<CompositeKeyContext>(
            Engine,
            static options => new CompositeKeyContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        foreach (var tenant in new[] { scope, otherScope })
        {
            await context.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new CompositeKeyNode
            {
                TenantId = tenant,
                RowId = depth + 100,
                NodeKey = depth + 42,
                ParentNodeKey = depth == 0 ? null : depth + 41,
                TreeId = treeId,
                Left = depth + 1,
                Right = 6 - depth,
                Depth = depth,
                Name = $"Node {depth}",
            }), CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositeKeyNode>().ForScope(scope).InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }

    /// <summary>
    /// An external row from another scope cannot be rebound to a local node with the same scalar key.
    /// </summary>
    /// <param name="operation">The row-filter composer receiving a foreign runtime endpoint.</param>
    [Theory]
    [InlineData(HierarchyCompositionOperation.AllowedPath)]
    [InlineData(HierarchyCompositionOperation.AnyAncestor)]
    [InlineData(HierarchyCompositionOperation.NearestAncestor)]
    public async Task ForeignScopeEndpointCannotAdoptLocalEqualNodeKeys(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var scope = NextIdentity();
        var otherScope = NextIdentity();
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<CompositeKeyContext>(
            Engine,
            static options => new CompositeKeyContext(options),
            probe);

        foreach (var tenant in new[] { scope, otherScope })
        {
            await context.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new CompositeKeyNode
            {
                TenantId = tenant,
                RowId = depth + 100,
                NodeKey = depth + 42,
                ParentNodeKey = depth == 0 ? null : depth + 41,
                TreeId = treeId,
                Left = depth + 1,
                Right = 6 - depth,
                Depth = depth,
                Name = $"Node {depth}",
            }), CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositeKeyNode>().ForScope(scope).InTree(treeId);
        var foreign = context.NestedSet<CompositeKeyNode>().ForScope(otherScope).InTree(treeId);
        var pairs = foreign.SelectSubtrees(node => node.Depth == 0,
                (anchor, node, distance) => new { Anchor = anchor, Node = node })
            .Where(row => row.Node.Depth == 2);

        // Act
        var query = operation switch
        {
            HierarchyCompositionOperation.AllowedPath => hierarchy.WherePathEdges(
                pairs, row => row.Anchor, row => row.Node, (row, parent, child) => true),
            HierarchyCompositionOperation.AnyAncestor => hierarchy.WhereAnyAncestorOrSelf(
                pairs, row => row.Node, (row, owner) => true),
            HierarchyCompositionOperation.NearestAncestor => hierarchy.WhereNearestAncestorOrSelf(
                pairs, row => row.Node, (row, owner) => true, (row, owner) => true),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var actual = await query.Select(row => row.Node.NodeKey).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
