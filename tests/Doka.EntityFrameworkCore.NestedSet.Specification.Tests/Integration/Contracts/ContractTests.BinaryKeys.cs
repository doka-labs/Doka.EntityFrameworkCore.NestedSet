namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ContractTests
{
    /// <summary>Verifies that validation resolves independently materialized binary parent keys by value.</summary>
    /// <returns>A task that completes when the validation result has been checked.</returns>
    [Fact]
    public async Task BinaryKeysUseValueEqualityForValidation()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await SeedBinaryHierarchyAsync(context);
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);

        // Act
        var errors = await tree
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Empty(errors.Issues);
    }

    /// <summary>Verifies that rebuild follows binary parent keys while repairing all structural values.</summary>
    /// <returns>A task that completes when the rebuilt boundaries have been verified.</returns>
    [Fact]
    public async Task BinaryKeysUseValueEqualityForRebuild()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await SeedBinaryHierarchyAsync(context);
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);
        await tree
            .InTree(Guid.Empty)
            .Nodes
            .ExecuteUpdateAsync(
                x => x
                    .SetProperty(n => n.Left, 1)
                    .SetProperty(n => n.Right, 2)
                    .SetProperty(n => n.Depth, 99),
                CancellationToken.None);

        // Act
        await tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None);
        var nodes = await tree
            .InTree(Guid.Empty)
            .Nodes
            .OrderBy(x => x.Left)
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_threeLevelChain, nodes.Select(x => (x.Left, x.Right, x.Depth, x.Position)));
        Assert.Equal(new byte[] { 1, 2 }, nodes[1].ParentId);
        Assert.Equal(new byte[] { 3, 4 }, nodes[2].ParentId);
    }

    /// <summary>Verifies that descendant queries accept a distinct array containing the persisted key bytes.</summary>
    /// <returns>A task that completes when both descendants have been found.</returns>
    [Fact]
    public async Task BinaryKeysUseValueEqualityForDescendants()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await SeedBinaryHierarchyAsync(context);
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);

        // Act
        var descendants = await tree
            .DescendantsOf<byte[]>([1, 2])
            .OrderBy(x => x.Left)
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, descendants.Count);
        Assert.Equal(new byte[] { 3, 4 }, descendants[0].Id);
        Assert.Equal(new byte[] { 5, 6 }, descendants[1].Id);
    }

    /// <summary>Verifies that a move resolves binary source and destination keys by their stored values.</summary>
    /// <returns>A task that completes when the reparented binary-key node has been verified.</returns>
    [Fact]
    public async Task BinaryKeysUseValueEqualityForMove()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await SeedBinaryHierarchyAsync(context);
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);

        // Act
        await tree.MoveToAsync<byte[]>([5, 6], [1, 2], CancellationToken.None);
        var nodes = await tree
            .InTree(Guid.Empty)
            .Nodes
            .OrderBy(x => x.Left)
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_rootAndTwoChildren, nodes.Select(x => (x.Left, x.Right, x.Depth, x.Position)));
        Assert.Equal(new byte[] { 1, 2 }, nodes[1].ParentId);
        Assert.Equal(new byte[] { 1, 2 }, nodes[2].ParentId);
    }

    /// <summary>Verifies that direct-child queries resolve an independently allocated binary parent key.</summary>
    /// <returns>A task that completes when the moved siblings have been found.</returns>
    [Fact]
    public async Task BinaryKeysUseValueEqualityForChildren()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await SeedBinaryHierarchyAsync(context);
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);
        await tree.MoveToAsync<byte[]>([5, 6], [1, 2], CancellationToken.None);

        // Act
        var children = await tree
            .ChildrenOf<byte[]>([1, 2])
            .OrderBy(x => x.Position)
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, children.Count);
        Assert.Equal(new byte[] { 3, 4 }, children[0].Id);
        Assert.Equal(new byte[] { 5, 6 }, children[1].Id);
    }
}
