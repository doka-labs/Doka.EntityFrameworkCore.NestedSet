namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>A complete foreign named mapping cannot repair an interrupted path with equal stored identities.</summary>
    /// <param name="filter">The independent, endpoint-correlated, or nearest path contract.</param>
    /// <param name="missingParent">Whether only the selected mapping lacks an intermediate Parent.</param>
    [Theory]
    [InlineData("independent", false)]
    [InlineData("independent", true)]
    [InlineData("correlated", false)]
    [InlineData("correlated", true)]
    [InlineData("nearest", false)]
    [InlineData("nearest", true)]
    public async Task LongNamedPathsPreserveExactMappingIdentity(
        string filter,
        bool missingParent
    )
    {
        // Arrange
        var first = NextIdentity();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<CompositionBagContext>(Engine,
            static options => new CompositionBagContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        foreach (var name in new[] { CompositionBagContext.First, CompositionBagContext.Second })
        {
            var nodes = context.Set<Dictionary<string, object?>>(name);

            foreach (var depth in Enumerable.Range(0, 6))
            {
                // WHY: The other named hierarchy has identical keys and bounds but a complete path;
                // only the explicitly selected mapping may supply the missing Parent witness.
                var interrupted = missingParent && name == CompositionBagContext.First && depth == 3;
                await nodes.AddAsync(new Dictionary<string, object?>
                {
                    [NestedSetPropertyRoles.NodeKey] = first + depth,
                    [NestedSetPropertyRoles.TreeId] = tree,
                    [NestedSetPropertyRoles.Parent] = depth == 0 || interrupted ? null : first + depth - 1,
                    [NestedSetPropertyRoles.Left] = (long)depth + 1,
                    [NestedSetPropertyRoles.Right] = 12L - depth,
                    [NestedSetPropertyRoles.Depth] = depth,
                    [NestedSetPropertyRoles.Position] = 0L,
                }, CancellationToken.None);
            }
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        materialized = 0;
        var hierarchy = context.NestedSet<Dictionary<string, object?>>(CompositionBagContext.First).InTree(tree);
        int[] expected = missingParent ? [] : filter == "correlated" ? [1] : [0, 1];

        // Act
        var actual = await QueryLongDepths(hierarchy, filter)
            .OrderBy(depth => depth).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, materialized);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
