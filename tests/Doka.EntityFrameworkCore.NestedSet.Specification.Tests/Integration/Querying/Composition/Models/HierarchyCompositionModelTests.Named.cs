namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Named indexer-backed mappings remain distinct even with equal keys, trees, and bounds.</summary>
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
    public async Task NamedIndexerMappingsPreserveExactHierarchyIdentity(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await CreateBagContextAsync(treeId, probe);
        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        var hierarchy = context.NestedSet<Dictionary<string, object?>>(CompositionBagContext.First).InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }

    /// <summary>Row filters reject aliases of another named mapping before sending any command.</summary>
    /// <param name="operation">The row-filter composer whose selected endpoint belongs to another mapping.</param>
    [Theory]
    [InlineData(HierarchyCompositionOperation.AllowedPath)]
    [InlineData(HierarchyCompositionOperation.AnyAncestor)]
    [InlineData(HierarchyCompositionOperation.NearestAncestor)]
    public async Task NamedMappingEndpointMismatchIsRejectedBeforeSql(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateBagContextAsync(treeId, probe);
        var hierarchy = context.NestedSet<Dictionary<string, object?>>(CompositionBagContext.First).InTree(treeId);
        var foreign = context.NestedSet<Dictionary<string, object?>>(CompositionBagContext.Second).InTree(treeId);
        var pairs = foreign.SelectSubtrees(node => true,
            (anchor, node, distance) => new { Anchor = anchor, Node = node });

        // Act
        var exception = Record.Exception(() => operation switch
        {
            HierarchyCompositionOperation.AllowedPath => hierarchy.WherePathEdges(
                pairs, row => row.Anchor, row => row.Node, (row, parent, child) => true),
            HierarchyCompositionOperation.AnyAncestor => hierarchy.WhereAnyAncestorOrSelf(
                pairs, row => row.Node, (row, owner) => true),
            HierarchyCompositionOperation.NearestAncestor => hierarchy.WhereNearestAncestorOrSelf(
                pairs, row => row.Node, (row, owner) => true, (row, owner) => true),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        });

        // Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Equal(0, probe.CommandCount);
    }

    /// <summary>Seeds both named tables without resetting rows owned by another theory case.</summary>
    private async Task<CompositionBagContext> CreateBagContextAsync(
        Guid treeId,
        CompositionCommandProbe probe
    )
    {
        var context = await _fixture.CreateContextAsync<CompositionBagContext>(
            Engine,
            static options => new CompositionBagContext(options),
            probe,
            TestMaterializationObserver.Instance);

        try
        {
            var first = NextIdentity();

            foreach (var name in new[] { CompositionBagContext.First, CompositionBagContext.Second })
            {
                var nodes = context.Set<Dictionary<string, object?>>(name);

                for (var depth = 0; depth < 3; depth++)
                {
                    await nodes.AddAsync(new Dictionary<string, object?>
                    {
                        [NestedSetPropertyRoles.NodeKey] = first + depth,
                        [NestedSetPropertyRoles.TreeId] = treeId,
                        [NestedSetPropertyRoles.Parent] = depth == 0 ? null : first + depth - 1,
                        [NestedSetPropertyRoles.Left] = (long)depth + 1,
                        [NestedSetPropertyRoles.Right] = 6L - depth,
                        [NestedSetPropertyRoles.Depth] = depth,
                        [NestedSetPropertyRoles.Position] = 0L,
                    }, CancellationToken.None);
                }
            }

            await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }
}
