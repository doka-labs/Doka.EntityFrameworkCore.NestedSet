namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    private static readonly int[] s_survivingConstrainedIds = [1, 4];

    /// <summary>Deleting a non-root promotes its children into its former sibling slot.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DeletePromotesChildrenIntoRemovedSiblingPosition()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        await tree.MoveBeforeAsync(5, 6, CancellationToken.None);
        await tree.MoveToAsync(3, 2, CancellationToken.None);

        // Act
        await tree.DeleteAsync(2, CancellationToken.None);

        // Assert
        await AssertOrderAsync(
            context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .Where(node => node.Parent == null)
                .OrderBy(node => node.NodeId),
            1);

        await AssertOrderAsync(tree.ChildrenOf(1), 4, 5, 6, 3);
        await AssertDepthsAsync(context, 1, (5, 1), (6, 1), (3, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>Deleting a subtree removes its descendants and compacts the surviving forest.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DeleteSubtreeRemovesAllDescendants()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        await tree.MoveBeforeAsync(5, 6, CancellationToken.None);
        await tree.MoveToAsync(3, 2, CancellationToken.None);
        await tree.DeleteAsync(2, CancellationToken.None);
        await tree.InsertRootAsync(Node(99), Guid.NewGuid(), CancellationToken.None);

        // Act
        await tree.DeleteSubtreeAsync(1, CancellationToken.None);

        // Assert
        await AssertOrderAsync(
            context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .Where(node => node.Parent == null)
                .OrderBy(node => node.NodeId),
            99);
        Assert.False(
            await context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .AnyAsync(x => x.NodeId == 4, CancellationToken.None));
        await AssertValidAsync(context, 1);
    }

    /// <summary>Promotion preserves child order, descendants and immediate-parent relations.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DeleteDeepNodePromotesAllChildSubtrees()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPromotionTreeAsync(context, 1);

        // Act
        await tree.DeleteAsync(3, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(2), 9, 4, 11, 10);
        await AssertDepthsAsync(context, 1, (4, 2), (5, 3), (11, 2), (12, 3));
        await AssertOrderAsync(tree.ParentOf(5), 4);
        await AssertOrderAsync(tree.ParentOf(12), 11);
        await AssertValidAsync(context, 1);
    }

    /// <summary>Subtree removal does not change the depth of surviving siblings.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DeleteDeepSubtreePreservesSiblingDepths()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPromotionTreeAsync(context, 1);

        await tree.DeleteAsync(3, CancellationToken.None);

        // Act
        await tree.DeleteSubtreeAsync(4, CancellationToken.None);

        // Assert
        await AssertOrderAsync(tree.ChildrenOf(2), 9, 11, 10);
        await AssertDepthsAsync(context, 1, (11, 2), (12, 3));
        await AssertValidAsync(context, 1);
    }

    /// <summary>Removing a non-root ancestor rebases every surviving descendant depth.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DeleteDeepAncestorRebasesEntireSurvivingSubtree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPromotionTreeAsync(context, 1);

        await tree.DeleteAsync(3, CancellationToken.None);
        await tree.DeleteSubtreeAsync(4, CancellationToken.None);

        // WHY: Promotion requires a surviving parent; a TreeId always retains exactly one root.
        await tree.InsertRootAsync(Node(99), Guid.NewGuid(), CancellationToken.None);
        await tree.MoveToAsync(1, 99, CancellationToken.None);

        // Act
        await tree.DeleteAsync(1, CancellationToken.None);

        // Assert
        await AssertOrderAsync(
            context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .Where(node => node.Parent == null)
                .OrderBy(node => node.NodeId),
            6,
            99);
        await AssertOrderAsync(tree.ChildrenOf(99), 2);
        await AssertDepthsAsync(context, 1, (2, 1), (11, 2), (12, 3));
        await AssertValidAsync(context, 1);
    }

    /// <summary>Subtree deletion breaks internal references before restrictive foreign-key checks.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DeleteSubtreeSupportsRestrictiveSelfForeignKey()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<ConstrainedNode>()
            .ForScope(1);

        await SeedConstrainedTreeAsync(context, 1);

        // Act
        await tree.DeleteSubtreeAsync(2, CancellationToken.None);

        // Assert
        var ids = await context
            .Set<ConstrainedNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .OrderBy(x => x.Left)
            .Select(x => x.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(s_survivingConstrainedIds, ids);
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Promotion updates the parent reference before deleting a referenced non-root.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DeletePromotesChildWithRestrictiveSelfForeignKey()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<ConstrainedNode>()
            .ForScope(1);

        await SeedConstrainedTreeAsync(context, 1);
        await tree.DeleteSubtreeAsync(2, CancellationToken.None);

        await tree.InsertRootAsync(new ConstrainedNode { Id = 5 }, Guid.NewGuid(), CancellationToken.None);
        await tree.MoveToAsync(1, 5, CancellationToken.None);

        // Act
        await tree.DeleteAsync(1, CancellationToken.None);

        // Assert
        var promoted = await context
            .Set<ConstrainedNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .SingleAsync(node => node.Id == 4, CancellationToken.None);

        Assert.Equal(4, promoted.Id);
        Assert.Equal(5, promoted.ParentId);
        Assert.Equal((2, 3, 1, 0), (promoted.Left, promoted.Right, promoted.Depth, promoted.Position));
        Assert.True(
            (await tree
                .InTree(promoted.TreeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Deleting the only root cannot promote children into several roots with the same TreeId.</summary>
    [Fact]
    public async Task DeleteRootPromotionIsRejectedWithoutChangingItsTree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);
        var before = await SnapshotAsync(context, 1);

        // Act
        var error = await Record.ExceptionAsync(() => tree.DeleteAsync(1, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.OperationRejected, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }
}
