namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Binary node and parent keys compare stored bytes rather than independently allocated arrays.</summary>
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
    public async Task BinaryKeysRetainDatabaseValueEqualityInEveryComposer(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var scope = NextIdentity();
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<CompositionBinaryContext>(
            Engine,
            static options => new CompositionBinaryContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        await context.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new BinaryNode
        {
            Id = BitConverter.GetBytes(scope + depth),
            ParentId = depth == 0 ? null : BitConverter.GetBytes(scope + depth - 1),
            Tree = scope,
            TreeId = treeId,
            Left = depth + 1,
            Right = 6 - depth,
            Depth = depth,
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<BinaryNode>().ForScope(scope).InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }
}
