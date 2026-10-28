namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Long TPT paths retain nullable Parent identity across alternating concrete payload types.</summary>
    /// <param name="filter">The independent, endpoint-correlated, or nearest path contract.</param>
    /// <param name="missingParent">Whether an intermediate derived node lacks its direct Parent.</param>
    [Theory]
    [InlineData("independent", false)]
    [InlineData("independent", true)]
    [InlineData("correlated", false)]
    [InlineData("correlated", true)]
    [InlineData("nearest", false)]
    [InlineData("nearest", true)]
    public async Task LongTptPathsRetainPolymorphicParentIdentity(
        string filter,
        bool missingParent
    )
    {
        // Arrange
        var first = NextIdentity();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<TptContext>(Engine,
            static options => new TptContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);

        foreach (var depth in Enumerable.Range(0, 6))
        {
            InheritanceNode node = depth % 2 == 0 ? new TptFolderNode() : new TptMetricNode();
            node.Id = first + depth;
            node.ParentId = depth == 0 || (missingParent && depth == 3) ? null : first + depth - 1;
            node.TreeId = tree;
            node.Left = depth + 1;
            node.Right = 12 - depth;
            node.Depth = depth;
            node.Name = $"Long inherited {depth}";
            await context.AddAsync(node, CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        materialized = 0;
        var hierarchy = context.NestedSet<InheritanceNode>().InTree(tree);
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
