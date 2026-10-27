namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>
    /// Converted operator-free keys support every composer and never make absent owners equal key zero.
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
    public async Task ConvertedOperatorFreeKeysPreserveNullableParentAndMissingOwnerSemantics(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<CompositionConvertedContext>(
            Engine,
            static options => new CompositionConvertedContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        await context.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new CompositionConvertedNode
        {
            Scope = scope,
            Id = new CompositionNodeKey(depth),
            ParentId = depth == 0 ? null : new CompositionNodeKey(depth - 1),
            TreeId = treeId,
            Left = depth + 1,
            Right = 6 - depth,
            Depth = depth,
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionConvertedNode>().ForScope(scope).InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }
}
