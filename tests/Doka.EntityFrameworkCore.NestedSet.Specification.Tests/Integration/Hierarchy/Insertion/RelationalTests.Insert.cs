namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Inserting an independent root starts local bounds and position at depth zero.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InsertRootStartsAnIndependentTreeAtDepthZero()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        await tree.InsertRootAsync(Node(7), Guid.NewGuid(), CancellationToken.None);

        // Assert
        await AssertOrderAsync(
            context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .Where(node => node.Parent == null)
                .OrderBy(node => node.NodeId),
            1,
            7);

        await AssertDepthsAsync(context, 1, (7, 0));
        var inserted = await tree
            .TreeContaining(7)
            .SingleAsync(CancellationToken.None);

        Assert.Equal((1L, 2L, 0L), (inserted.Start, inserted.End, inserted.Position));
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1);
    }

    /// <summary>A child is inserted at the requested end of its sibling group.</summary>
    /// <param name="first">Whether the new child belongs before its siblings.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertChildRespectsRequestedPosition(
        bool first
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        await (first
            ? tree.InsertAsFirstChildAsync(Node(7), 1, CancellationToken.None)
            : tree.InsertAsLastChildAsync(Node(7), 1, CancellationToken.None));

        // Assert
        var expected = first
            ? new[] { 7, 3, 4, 2, 5 }
            : new[] { 3, 4, 2, 5, 7 };

        await AssertOrderAsync(tree.ChildrenOf(1), expected);
        await AssertDepthsAsync(context, 1, (7, 1));
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1);
    }

    /// <summary>Sibling insertion preserves the parent and shifts following positions.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InsertBeforeUsesSiblingPosition()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        await tree.InsertBeforeAsync(Node(7), 2, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(1), 3, 4, 7, 2, 5);
        await AssertDepthsAsync(context, 1, (7, 1));
        await AssertOrderAsync(tree.ParentOf(7), 1);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1);
    }

    /// <summary>Sibling insertion preserves the parent and shifts following positions.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InsertAfterUsesSiblingPosition()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        await tree.InsertAfterAsync(Node(7), 2, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(1), 3, 4, 2, 7, 5);
        await AssertDepthsAsync(context, 1, (7, 1));
        await AssertOrderAsync(tree.ParentOf(7), 1);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1);
    }

    /// <summary>Concurrent first writers create independent valid trees without preexisting registry rows.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ConcurrentFirstInsertsCreateIndependentTrees()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await using var secondContext = database.CreateContext();
        var secondTree = secondContext
            .NestedSet<TreeNode>()
            .ForScope(1);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstInsert = InsertAfterGateAsync(tree, 1, gate.Task);
        var secondInsert = InsertAfterGateAsync(secondTree, 2, gate.Task);

        // Act
        gate.SetResult();
        await Task.WhenAll(firstInsert, secondInsert);

        // Assert
        Assert.Equal(
            2,
            await context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .CountAsync(CancellationToken.None));
        await AssertValidAsync(context, 1);
    }
}
