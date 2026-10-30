namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Long binary-key paths use stored Parent bytes rather than CLR array reference equality.</summary>
    /// <param name="filter">The independent, endpoint-correlated, or nearest path contract.</param>
    /// <param name="missingParent">Whether the intermediate depth-three node lacks its binary Parent.</param>
    [Theory]
    [InlineData("independent", false)]
    [InlineData("independent", true)]
    [InlineData("correlated", false)]
    [InlineData("correlated", true)]
    [InlineData("nearest", false)]
    [InlineData("nearest", true)]
    public async Task LongBinaryPathsRetainDatabaseParentValueEquality(
        string filter,
        bool missingParent
    )
    {
        // Arrange
        var scope = NextIdentity();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<CompositionBinaryContext>(Engine,
            static options => new CompositionBinaryContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        await context.AddRangeAsync(Enumerable.Range(0, 6).Select(depth => new BinaryNode
        {
            Id = BitConverter.GetBytes(scope + depth),
            ParentId = depth == 0 || (missingParent && depth == 3) ? null : BitConverter.GetBytes(scope + depth - 1),
            Tree = scope,
            TreeId = tree,
            Left = depth + 1,
            Right = 12 - depth,
            Depth = depth,
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        materialized = 0;
        var hierarchy = context.NestedSet<BinaryNode>().ForScope(scope).InTree(tree);
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
