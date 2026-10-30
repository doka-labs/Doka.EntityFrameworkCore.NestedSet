namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Polymorphic TPH and TPT queries retain direct edges across different concrete node types.</summary>
    /// <param name="tpt">Whether payload occupies separate TPT tables rather than a TPH discriminator table.</param>
    /// <param name="operation">The individual public query contract or rejection case.</param>
    [Theory]
    [InlineData(false, HierarchyCompositionOperation.Ancestors)]
    [InlineData(false, HierarchyCompositionOperation.AncestorsOrSelf)]
    [InlineData(false, HierarchyCompositionOperation.Subtrees)]
    [InlineData(false, HierarchyCompositionOperation.AllowedPath)]
    [InlineData(false, HierarchyCompositionOperation.AnyAncestor)]
    [InlineData(false, HierarchyCompositionOperation.NearestAncestor)]
    [InlineData(false, HierarchyCompositionOperation.MissingNearestOwner)]
    [InlineData(false, HierarchyCompositionOperation.NearestOwnerRejectsValue)]
    [InlineData(false, HierarchyCompositionOperation.BlockedPath)]
    [InlineData(true, HierarchyCompositionOperation.Ancestors)]
    [InlineData(true, HierarchyCompositionOperation.AncestorsOrSelf)]
    [InlineData(true, HierarchyCompositionOperation.Subtrees)]
    [InlineData(true, HierarchyCompositionOperation.AllowedPath)]
    [InlineData(true, HierarchyCompositionOperation.AnyAncestor)]
    [InlineData(true, HierarchyCompositionOperation.NearestAncestor)]
    [InlineData(true, HierarchyCompositionOperation.MissingNearestOwner)]
    [InlineData(true, HierarchyCompositionOperation.NearestOwnerRejectsValue)]
    [InlineData(true, HierarchyCompositionOperation.BlockedPath)]
    public async Task InheritedMappingsRetainPolymorphicIdentityAndCompletePaths(
        bool tpt,
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var first = NextIdentity();
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using DbContext context = tpt
            ? await _fixture.CreateContextAsync<TptContext>(Engine, static options => new TptContext(options),
                probe, TestMaterializationObserver.Instance)
            : await _fixture.CreateContextAsync<TphContext>(Engine, static options => new TphContext(options),
                probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        for (var depth = 0; depth < 3; depth++)
        {
            InheritanceNode node = tpt
                ? depth == 1 ? new TptMetricNode() : new TptFolderNode()
                : depth == 1 ? new TphMetricNode() : new TphFolderNode();

            node.Id = first + depth;
            node.ParentId = depth == 0 ? null : first + depth - 1;
            node.TreeId = treeId;
            node.Left = depth + 1;
            node.Right = 6 - depth;
            node.Depth = depth;
            node.Name = $"Node {depth}";
            await context.AddAsync(node, CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<InheritanceNode>().InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }

    /// <summary>
    /// Concrete TPC hierarchies compose within their own mapped table without a polymorphic fallback.
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
    public async Task ConcreteTpcMappingRetainsExactEntityIdentity(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var first = NextIdentity();
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<ConcreteTpcContext>(
            Engine,
            static options => new ConcreteTpcContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        await context.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new TpcFolderNode
        {
            Id = first + depth,
            ParentId = depth == 0 ? null : first + depth - 1,
            TreeId = treeId,
            Left = depth + 1,
            Right = 6 - depth,
            Depth = depth,
            Name = $"Node {depth}",
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<TpcFolderNode>().InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }
}
