namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Moving a deep subtree updates every descendant level and its structural relation.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveToAsync_MovingSubtreeDown_IncreasesAllDescendantDepths()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await tree.MoveToAsync(3, 8, CancellationToken.None);

        // Assert
        await AssertDepthsAsync(
            context,
            1,
            cancellationToken: TestContext.Current.CancellationToken,
            (3, 3),
            (4, 4),
            (5, 5));

        await AssertOrderAsync(
            tree.AncestorsOf(5),
            cancellationToken: TestContext.Current.CancellationToken,
            6,
            7,
            8,
            3,
            4);

        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Moving a deep subtree updates every descendant level and its structural relation.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveToAsync_MovingSubtreeUp_DecreasesAllDescendantDepths()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
        await tree.MoveToAsync(3, 8, CancellationToken.None);

        // Act
        await tree.MoveToAsync(3, 1, CancellationToken.None);

        // Assert
        await AssertDepthsAsync(
            context,
            1,
            cancellationToken: TestContext.Current.CancellationToken,
            (3, 1),
            (4, 2),
            (5, 3));

        await AssertOrderAsync(tree.ChildrenOf(1), cancellationToken: TestContext.Current.CancellationToken, 2, 3);
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Moving a deep subtree updates every descendant level and its structural relation.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveAfterAsync_MovingDeepSubtreeAfterSibling_PreservesDepth()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
        await tree.MoveBeforeAsync(3, 2, CancellationToken.None);

        // Act
        await tree.MoveAfterAsync(3, 2, CancellationToken.None);

        // Assert
        await AssertDepthsAsync(
            context,
            1,
            cancellationToken: TestContext.Current.CancellationToken,
            (3, 1),
            (4, 2),
            (5, 3));

        await AssertOrderAsync(tree.ChildrenOf(1), cancellationToken: TestContext.Current.CancellationToken, 2, 3);
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Moving a branch into another tree updates every descendant depth.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveToAsync_MovingDeepSubtreeToEmptyParent_UpdatesDepth()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await tree.MoveToAsync(3, 8, CancellationToken.None);

        // Assert
        await AssertDepthsAsync(
            context,
            1,
            cancellationToken: TestContext.Current.CancellationToken,
            (3, 3),
            (4, 4),
            (5, 5));

        await AssertOrderAsync(tree.ChildrenOf(8), cancellationToken: TestContext.Current.CancellationToken, 3);
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Detaching a deep branch rebases its root and every descendant depth.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DetachAsTreeAsync_DetachingDeepSubtree_RebasesEveryDescendantDepth()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await tree.DetachAsTreeAsync(3, Guid.NewGuid(), CancellationToken.None);

        // Assert
        await AssertDepthsAsync(
            context,
            1,
            cancellationToken: TestContext.Current.CancellationToken,
            (3, 0),
            (4, 1),
            (5, 2));

        await AssertOrderAsync(
            context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .Where(node => node.Parent == null)
                .OrderBy(node => node.NodeId),
            cancellationToken: TestContext.Current.CancellationToken,
            1,
            3,
            6);

        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }
}
