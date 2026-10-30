namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>
    /// Converted reference TreeIds retain database identity even when CLR equality treats aliases as equal.
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
    public async Task ConvertedTreeIdentityKeepsDatabaseDistinctAliasesIsolated(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var first = NextIdentity();
        var token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var treeId = new BroadTreeId(token + "a");
        var otherTree = new BroadTreeId(token + "A");
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<BroadTreeIdContext>(
            Engine,
            static options => new BroadTreeIdContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        foreach (var (identity, offset) in new[] { (treeId, 0), (otherTree, 3) })
        {
            await context.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new BroadTreeIdNode
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
        var hierarchy = context.NestedSet<BroadTreeIdNode>().InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }
}
