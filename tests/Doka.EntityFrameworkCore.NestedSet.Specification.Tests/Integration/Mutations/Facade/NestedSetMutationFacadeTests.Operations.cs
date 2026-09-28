namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetMutationFacadeTests
{
    /// <summary>Manual sibling insertion preserves dense positions and preorder.</summary>
    [Fact]
    public async Task ManualInsertionMaintainsSiblingPositions()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertAfterAsync(new TreeNode { NodeId = 4 }, 2, CancellationToken.None);

        // Act
        await hierarchy.InsertBeforeAsync(new TreeNode { NodeId = 3 }, 4, CancellationToken.None);
        var children = await hierarchy
            .ChildrenOf(1)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([2, 3, 4], children.Select(node => node.NodeId));
        Assert.Equal([0L, 1L, 2L], children.Select(node => node.Position));
    }

    /// <summary>Same-tree moves permute only the crossed sibling interval.</summary>
    [Fact]
    public async Task MoveBeforeMaintainsDenseSiblingOrder()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 3 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 4 }, 1, CancellationToken.None);

        // Act
        await hierarchy.MoveBeforeAsync(4, 2, CancellationToken.None);
        var children = await hierarchy
            .ChildrenOf(1)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([4, 2, 3], children.Select(node => node.NodeId));
        Assert.Equal([0L, 1L, 2L], children.Select(node => node.Position));
    }

    /// <summary>Deleting a non-root promotes its children as one dense sibling block.</summary>
    [Fact]
    public async Task DeletePromotesDirectChildren()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 3 }, 2, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 4 }, 2, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 5 }, 1, CancellationToken.None);

        // Act
        await hierarchy.DeleteAsync(2, CancellationToken.None);
        var nodes = await hierarchy
            .InTree(s_firstTree)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 3, 4, 5], nodes.Select(node => node.NodeId));
        Assert.All(nodes.Skip(1), node => Assert.Equal(1, node.Parent));
        Assert.Equal([0L, 1L, 2L], nodes.Skip(1).Select(node => node.Position));
    }

    /// <summary>DeleteTree removes only the selected identity and tombstones it.</summary>
    [Fact]
    public async Task DeleteTreeLeavesOtherTreeUntouched()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);

        // Act
        await hierarchy.DeleteTreeAsync(s_firstTree, CancellationToken.None);
        var remaining = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal((10, s_secondTree, 1L, 2L), (remaining.NodeId, remaining.TreeId, remaining.Start, remaining.End));
    }

    /// <summary>Moving a complete source tree retires its old identity after the merge.</summary>
    [Fact]
    public async Task CrossTreeRootMoveTombstonesSourceIdentity()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);

        // Act
        await hierarchy.MoveToAsync(1, 10, CancellationToken.None);
        var moved = await hierarchy
            .InTree(s_secondTree)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([10, 1, 2], moved.Select(node => node.NodeId));
        Assert.Empty(
            await hierarchy
                .InTree(s_firstTree)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>A source TreeId retired by a complete cross-tree move cannot be reused.</summary>
    [Fact]
    public async Task CrossTreeRootMoveSourceIdCannotBeReused()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);
        await hierarchy.MoveToAsync(1, 10, CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.InsertRootAsync(new TreeNode { NodeId = 20 }, s_firstTree, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, error.Code);
    }
}
