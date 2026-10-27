namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>
    /// A table-shared hierarchy principal composes without materializing its optional payload entity.
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
    public async Task TableSplitPrincipalRetainsCompletePathsWithoutPayloadMaterialization(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var first = NextIdentity();
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<TableSplitContext>(
            Engine,
            static options => new TableSplitContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        for (var depth = 0; depth < 3; depth++)
        {
            await context.AddRangeAsync(
            [
                new TableSplitNode
                {
                    Id = first + depth,
                    ParentId = depth == 0 ? null : first + depth - 1,
                    TreeId = treeId,
                    Left = depth + 1,
                    Right = 6 - depth,
                    Depth = depth,
                    Name = $"Node {depth}",
                },
                new TableSplitPayload { Id = first + depth, Description = "Optional application payload" },
            ], CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<TableSplitNode>().InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }

    /// <summary>Entity splitting retains identity when structural values occupy the secondary fragment.</summary>
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
    public async Task EntitySplitStructuralFragmentRetainsMappedIdentityAndCompletePaths(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var first = NextIdentity();
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<EntitySplitContext>(
            Engine,
            static options => new EntitySplitContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        await context.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new EntitySplitNode
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
        var hierarchy = context.NestedSet<EntitySplitNode>().InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }
}
