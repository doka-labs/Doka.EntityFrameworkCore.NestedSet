namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AutomaticMoveBudgetTests
{
    /// <summary>A deeper destination resolves TreeId once and reuses its locked parent for depth checks.</summary>
    [Theory]
    [InlineData("Strict")]
    [InlineData("Flexible")]
    public async Task DeeperDestinationPreservesSubtreeDepthWithOneParentRead(
        string mode
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine, mode);
        await SeedAsync(setup);
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, mode, probe);
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        // Act
        await tree.MoveToAsync(3, 5, CancellationToken.None);

        // Assert
        Assert.Single(FacadeIdentityReads(probe, mode, context));
        var reads = LockedHierarchyReads(probe, mode, context);

        Assert.Equal(5, reads.Length);
        Assert.Single(reads, sql => sql.Contains("MAX(", StringComparison.OrdinalIgnoreCase));
        Assert.Single(reads, sql => sql.Contains("LAG(", StringComparison.OrdinalIgnoreCase));
        var subtree = await tree
            .SubtreeOf(3)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([3, 4], subtree.Select(node => node.Id));
        Assert.Equal([5, 3], subtree.Select(node => node.ParentId));
        Assert.Equal([3, 4], subtree.Select(node => node.Depth));
        Assert.All(subtree, node => Assert.Equal(0, node.Position));
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }
}
