namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Every composer honors renamed shadow roles instead of misleading conventional CLR properties.</summary>
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
    public async Task RenamedShadowRolesPreserveIdentityAndRejectMissingOwners(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<CompositionShadowContext>(
            Engine,
            static options => new CompositionShadowContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        for (var depth = 0; depth < 3; depth++)
        {
            var node = new CompositionShadowNode { Label = $"Node {depth}" };
            // WHY: Assign the full shadow key before tracking, so the next child never temporarily aliases root zero.
            var entry = context.Entry(node);
            entry.Property(CompositionShadowContext.Scope).CurrentValue = scope;
            entry.Property(CompositionShadowContext.Key).CurrentValue = depth;
            entry.Property(CompositionShadowContext.Tree).CurrentValue = treeId;
            entry.Property(CompositionShadowContext.Parent).CurrentValue = depth == 0 ? null : depth - 1;
            entry.Property(CompositionShadowContext.Start).CurrentValue = (long)depth + 1;
            entry.Property(CompositionShadowContext.End).CurrentValue = 6L - depth;
            entry.Property(CompositionShadowContext.Depth).CurrentValue = depth;
            entry.Property(CompositionShadowContext.Position).CurrentValue = 0L;
            await context.AddAsync(node, CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionShadowNode>().ForScope(scope).InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation, CompositionShadowContext.Depth)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
    }
}
