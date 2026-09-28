namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    private static readonly int[] s_deepTreeDepths = [0, 1, 2, 3, 4, 0, 1, 2];

    /// <summary>Moving a subtree before a sibling preserves dense sibling positions.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveBeforeShiftsSiblingPositions()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context.NestedSet<TreeNode>().ForScope(1);
        await SeedPlacementTreeAsync(context, 1);

        // Act
        await tree.MoveBeforeAsync(2, 3, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(1), 2, 3, 4, 5);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1);
    }

    /// <summary>Moving a subtree after a sibling preserves dense sibling positions.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveAfterShiftsSiblingPositions()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        await tree.MoveAfterAsync(2, 5, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(1), 3, 4, 5, 2);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1);
    }

    /// <summary>Moving before the first existing child prepends the complete subtree.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveBeforePrependsSubtreeToDestinationChildren()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        await tree.MoveBeforeAsync(5, 6, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(2), 5, 6);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1);
    }

    /// <summary>Moving to a parent appends the subtree when manual ordering is configured.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveToAppendsSubtreeToDestinationChildren()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        await tree.MoveToAsync(3, 2, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(2), 6, 3);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertValidAsync(context, 1);
    }

    /// <summary>Detaching a subtree creates a fresh TreeId with local bounds and root position zero.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DetachAsTreeCreatesIndependentRoot()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

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
        await AssertValidAsync(context, 1);
    }

    /// <summary>A hierarchy stores root-zero depth through five levels.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InsertedDeepHierarchyStoresEveryLevel()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
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
        await AssertValidAsync(context, 1);
    }

    /// <summary>Moving a deep subtree updates every descendant level and its structural relation.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MovingSubtreeDownIncreasesAllDescendantDepths()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1);

        // Act
        await tree.MoveToAsync(3, 8, CancellationToken.None);

        // Assert
        await AssertDepthsAsync(context, 1, (3, 3), (4, 4), (5, 5));
        await AssertOrderAsync(tree.AncestorsOf(5), 6, 7, 8, 3, 4);
        await AssertValidAsync(context, 1);
    }

    /// <summary>Moving a deep subtree updates every descendant level and its structural relation.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MovingSubtreeUpDecreasesAllDescendantDepths()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1);
        await tree.MoveToAsync(3, 8, CancellationToken.None);

        // Act
        await tree.MoveToAsync(3, 1, CancellationToken.None);

        // Assert
        await AssertDepthsAsync(context, 1, (3, 1), (4, 2), (5, 3));
        await AssertOrderAsync(tree.ChildrenOf(1), 2, 3);
        await AssertValidAsync(context, 1);
    }

    /// <summary>Moving a deep subtree updates every descendant level and its structural relation.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MovingDeepSubtreeAfterSiblingPreservesDepth()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1);
        await tree.MoveBeforeAsync(3, 2, CancellationToken.None);

        // Act
        await tree.MoveAfterAsync(3, 2, CancellationToken.None);

        // Assert
        await AssertDepthsAsync(context, 1, (3, 1), (4, 2), (5, 3));
        await AssertOrderAsync(tree.ChildrenOf(1), 2, 3);
        await AssertValidAsync(context, 1);
    }

    /// <summary>Moving a branch into another tree updates every descendant depth.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MovingDeepSubtreeToEmptyParentUpdatesDepth()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1);

        // Act
        await tree.MoveToAsync(3, 8, CancellationToken.None);

        // Assert
        await AssertDepthsAsync(context, 1, (3, 3), (4, 4), (5, 5));
        await AssertOrderAsync(tree.ChildrenOf(8), 3);
        await AssertValidAsync(context, 1);
    }

    /// <summary>Detaching a deep branch rebases its root and every descendant depth.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DetachingDeepSubtreeRebasesEveryDescendantDepth()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1);

        // Act
        await tree.DetachAsTreeAsync(3, Guid.NewGuid(), CancellationToken.None);

        // Assert
        await AssertDepthsAsync(context, 1, (3, 0), (4, 1), (5, 2));
        await AssertOrderAsync(
            context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .Where(node => node.Parent == null)
                .OrderBy(node => node.NodeId),
            1,
            3,
            6);
        await AssertValidAsync(context, 1);
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
    public async Task MutationPreservesOtherScope(
        string operation
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        var isolated = context
            .NestedSet<TreeNode>()
            .ForScope(2);

        await isolated.InsertRootAsync(Node(99), Guid.Empty, CancellationToken.None);
        await isolated.InsertChildAsync(Node(100), 99, cancellationToken: CancellationToken.None);
        await isolated.InsertChildAsync(Node(101), 100, cancellationToken: CancellationToken.None);
        var before = await SnapshotAsync(context, 2);

        // Act
        await ApplyScopedMutationAsync(context, 1, operation);

        // Assert
        Assert.Equal(before, await SnapshotAsync(context, 2));
        await AssertValidAsync(context, 1);
        await AssertValidAsync(context, 2);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
