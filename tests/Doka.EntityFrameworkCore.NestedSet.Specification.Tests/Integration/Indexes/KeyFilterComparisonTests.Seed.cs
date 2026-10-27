namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class KeyFilterComparisonTests
{
    /// <summary>Independent comparison contexts reuse the same fixture-owned immutable database.</summary>
    [Fact]
    public async Task ComparisonSeedIsReusedAcrossContexts()
    {
        // Arrange
        var first = await _fixture.ComparisonAsync(Engine);
        await using var firstContext = first.CreateContext();

        // Act
        var second = await _fixture.ComparisonAsync(Engine);
        await using var secondContext = second.CreateContext();
        var rows = await secondContext
            .Set<TreeNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Same(first, second);
        Assert.NotSame(firstContext, secondContext);
        Assert.Equal(256, rows);
    }

    /// <summary>A sibling reset cannot delete rows retained for independently discovered comparisons.</summary>
    [Fact]
    public async Task ResettingSiblingRowsPreservesComparisonSeed()
    {
        // Arrange
        var comparison = await _fixture.ComparisonAsync(Engine);

        // Act
        var mutable = await _fixture.ResetAsync(Engine);
        await using var comparisonContext = comparison.CreateContext();
        await using var mutableContext = mutable.CreateContext();
        var integerRows = await comparisonContext
            .Set<TreeNode>()
            .CountAsync(CancellationToken.None);

        var guidRows = await comparisonContext
            .Set<GuidNode>()
            .CountAsync(CancellationToken.None);

        var binaryRows = await comparisonContext
            .Set<BinaryNode>()
            .CountAsync(CancellationToken.None);

        var stringRows = await comparisonContext
            .Set<TextNode>()
            .CountAsync(CancellationToken.None);

        var resetRows = await mutableContext
            .Set<TreeNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.NotSame(comparison, mutable);
        Assert.Equal((256, 256, 256, 256), (integerRows, guidRows, binaryRows, stringRows));
        Assert.Equal(0, resetRows);
    }
}
