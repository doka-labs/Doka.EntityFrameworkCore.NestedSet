namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class WriteAmplificationTests
{
    /// <summary>Parent lookup joins its scoped anchor without imposing an order on the result.</summary>
    [Fact]
    public async Task ParentQueryJoinsTheScopedAnchorWithoutOrdering()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new StructuralWriteProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var parent = await service
            .ParentOf(3)
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([2], parent);
        Assert.All(probe.Commands, sql => Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("JOIN", probe.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORDER BY", probe.Commands[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ancestor lookup keeps its scoped anchor and returns every ancestor of the node.</summary>
    [Fact]
    public async Task AncestorQueryReturnsTheScopedPathWithoutExistenceScans()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new StructuralWriteProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var ancestors = await service
            .AncestorsOf(3)
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([900000, 1, 2], ancestors);
        Assert.All(probe.Commands, sql => Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Descendant lookup applies a depth filter within the selected tree and scope.</summary>
    [Fact]
    public async Task DescendantQueryPreservesDepthFilterWithoutExistenceScans()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new StructuralWriteProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var descendants = await service
            .DescendantsOf(1)
            .Where(node => node.Depth == 3)
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([3, 5], descendants);
        Assert.All(probe.Commands, sql => Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A node in another scope cannot provide the parent anchor for this service.</summary>
    [Fact]
    public async Task ParentQueryExcludesAnAnchorInAnotherScope()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new StructuralWriteProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var missing = await service
            .ParentOf(1000)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(missing);
        Assert.All(probe.Commands, sql => Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase));
    }
}
