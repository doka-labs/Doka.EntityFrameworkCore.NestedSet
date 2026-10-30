namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Long paths use renamed shadow roles and reject a missing intermediate shadow Parent.</summary>
    /// <param name="filter">The independent, endpoint-correlated, or nearest path contract.</param>
    /// <param name="missingParent">Whether the intermediate depth-three node lacks its direct Parent.</param>
    [Theory]
    [InlineData("independent", false)]
    [InlineData("independent", true)]
    [InlineData("correlated", false)]
    [InlineData("correlated", true)]
    [InlineData("nearest", false)]
    [InlineData("nearest", true)]
    public async Task LongShadowPathsRetainRenamedStructuralIdentity(
        string filter,
        bool missingParent
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<CompositionShadowContext>(Engine,
            static options => new CompositionShadowContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        foreach (var depth in Enumerable.Range(0, 6))
        {
            var node = new CompositionShadowNode { Label = $"Long shadow {depth}" };
            // WHY: Set the composite shadow key before tracking so descendants cannot temporarily alias root zero.
            var entry = context.Entry(node);
            entry.Property(CompositionShadowContext.Scope).CurrentValue = scope;
            entry.Property(CompositionShadowContext.Key).CurrentValue = depth;
            entry.Property(CompositionShadowContext.Tree).CurrentValue = tree;
            entry.Property(CompositionShadowContext.Parent).CurrentValue = depth == 0 || (missingParent && depth == 3)
                ? null : depth - 1;

            entry.Property(CompositionShadowContext.Start).CurrentValue = (long)depth + 1;
            entry.Property(CompositionShadowContext.End).CurrentValue = 12L - depth;
            entry.Property(CompositionShadowContext.Depth).CurrentValue = depth;
            entry.Property(CompositionShadowContext.Position).CurrentValue = 0L;
            await context.AddAsync(node, CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        materialized = 0;
        var hierarchy = context.NestedSet<CompositionShadowNode>().ForScope(scope).InTree(tree);
        int[] expected = missingParent ? [] : filter == "correlated" ? [1] : [0, 1];

        // Act
        var actual = await QueryLongDepths(hierarchy, filter, CompositionShadowContext.Depth)
            .OrderBy(depth => depth).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, materialized);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
