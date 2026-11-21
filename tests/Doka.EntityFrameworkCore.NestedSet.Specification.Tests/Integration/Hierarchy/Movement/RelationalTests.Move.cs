namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    private static readonly int[] s_deepTreeDepths = [0, 1, 2, 3, 4, 0, 1, 2];

    /// <summary>Moving a subtree before a sibling preserves dense sibling positions.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveBeforeAsync_MoveBefore_ShiftsSiblingPositions()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await tree.MoveBeforeAsync(2, 3, CancellationToken.None);

        // Assert
        await AssertOrderAsync(
            tree.ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            2,
            3,
            4,
            5);

        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Moving a subtree after a sibling preserves dense sibling positions.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveAfterAsync_MoveAfter_ShiftsSiblingPositions()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await tree.MoveAfterAsync(2, 5, CancellationToken.None);

        // Assert
        await AssertOrderAsync(
            tree.ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            3,
            4,
            5,
            2);

        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Moving before the first existing child prepends the complete subtree.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveBeforeAsync_MoveBefore_PrependsSubtreeToDestinationChildren()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await tree.MoveBeforeAsync(5, 6, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(2), cancellationToken: TestContext.Current.CancellationToken, 5, 6);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Moving to a parent appends the subtree when manual ordering is configured.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveToAsync_MoveTo_AppendsSubtreeToDestinationChildren()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await tree.MoveToAsync(3, 2, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(2), cancellationToken: TestContext.Current.CancellationToken, 6, 3);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Detaching a subtree creates a fresh TreeId with local bounds and root position zero.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DetachAsTreeAsync_DetachAsTree_CreatesIndependentRoot()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        var detachedTreeId = Guid.NewGuid();

        // Act
        await tree.DetachAsTreeAsync(2, detachedTreeId, CancellationToken.None);

        // Assert
        await AssertOrderAsync(
            context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .Where(node => node.Parent == null)
                .OrderBy(node => node.NodeId),
            cancellationToken: TestContext.Current.CancellationToken,
            1,
            2);

        var detached = await tree
            .InTree(detachedTreeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([2, 6], detached.Select(node => node.NodeId));
        Assert.Equal((1L, 4L, 0, 0L), (detached[0].Start, detached[0].End, detached[0].Depth, detached[0].Position));
        Assert.All(detached, node => Assert.Equal(detachedTreeId, node.TreeId));
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>A hierarchy stores root-zero depth through five levels.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InsertChildAsync_InsertedDeepHierarchy_StoresEveryLevel()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(Node(2), 1, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(3), 2, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(4), 3, cancellationToken: CancellationToken.None);
        await tree.InsertRootAsync(Node(6), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(7), 6, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(8), 7, cancellationToken: CancellationToken.None);

        // Act
        await tree.InsertChildAsync(Node(5), 4, cancellationToken: CancellationToken.None);

        // Assert
        var nodes = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .OrderBy(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(s_deepTreeDepths, nodes.Select(x => x.Depth));
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Every mutation leaves another scoped hierarchy byte-for-byte unchanged.</summary>
    /// <param name="operation">The single operation performed in the first scope.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("InsertRoot")]
    [InlineData("InsertFirstChild")]
    [InlineData("InsertLastChild")]
    [InlineData("InsertBefore")]
    [InlineData("InsertAfter")]
    [InlineData("MoveBefore")]
    [InlineData("MoveAfter")]
    [InlineData("MoveFirstChild")]
    [InlineData("MoveLastChild")]
    [InlineData("DetachAsTree")]
    [InlineData("Delete")]
    [InlineData("DeleteSubtree")]
    [InlineData("Rebuild")]
    public async Task ApplyScopedMutationAsync_Mutation_PreservesOtherScope(
        string operation
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        var isolated = context
            .NestedSet<TreeNode>()
            .ForScope(2);

        await isolated.InsertRootAsync(Node(99), Guid.Empty, CancellationToken.None);
        await isolated.InsertChildAsync(Node(100), 99, cancellationToken: CancellationToken.None);
        await isolated.InsertChildAsync(Node(101), 100, cancellationToken: CancellationToken.None);
        var before = await SnapshotAsync(context, 2, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await ApplyScopedMutationAsync(context, 1, operation, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(before, await SnapshotAsync(context, 2, cancellationToken: TestContext.Current.CancellationToken));
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
        await AssertValidAsync(context, 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
