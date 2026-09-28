namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetMutationFacadeTests
{
    /// <summary>Explicit mutations reject a tracked entity from the affected tree before changing the database.</summary>
    [Fact]
    public async Task MutationRejectsTrackedEntityFromAffectedTree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 2, CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertChildAsync(
            new TreeNode { NodeId = 3 },
            1,
            CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(error);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal(2, await context.Set<TreeNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>Explicit mutations allow unchanged tracked entities belonging only to an unaffected tree.</summary>
    [Fact]
    public async Task MutationAllowsTrackedEntityFromUnaffectedTree()
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
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 10, CancellationToken.None);

        // Act
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 3 }, 1, CancellationToken.None);
        var firstTree = await hierarchy
            .InTree(s_firstTree)
            .Nodes
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal((s_secondTree, 1L, 2L), (tracked.TreeId, tracked.Start, tracked.End));
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal([1, 2, 3], firstTree);
    }

    /// <summary>Subtree import accepts an unchanged tracked node in another tree.</summary>
    [Fact]
    public async Task SubtreeImportAllowsTrackedEntityFromUnaffectedTree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 10, CancellationToken.None);

        // Act
        await hierarchy.InsertSubtreeAsync(
            new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 2 }),
            1,
            CancellationToken.None);

        var ids = await hierarchy
            .InTree(s_firstTree)
            .Nodes
            .OrderBy(node => node.Start)
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal([1, 2], ids);
    }

    /// <summary>Repair of one exact tree accepts an unchanged tracked node in another tree.</summary>
    [Fact]
    public async Task RebuildAllowsTrackedEntityFromUnaffectedTree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 10, CancellationToken.None);

        // Act
        await hierarchy
            .InTree(s_firstTree)
            .RebuildAsync(CancellationToken.None);

        // Assert
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal((1L, 2L), (tracked.Start, tracked.End));
    }

    /// <summary>Subtree import still rejects tracking in the tree it will rewrite.</summary>
    [Fact]
    public async Task SubtreeImportRejectsTrackedEntityFromAffectedTree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        _ = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 1, CancellationToken.None);

        // Act
        var failure = await Record.ExceptionAsync(() => hierarchy.InsertSubtreeAsync(
            new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 2 }),
            1,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(1, await context.Set<TreeNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>Rebuild still rejects tracking in the tree it will repair.</summary>
    [Fact]
    public async Task RebuildRejectsTrackedEntityFromAffectedTree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        _ = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 1, CancellationToken.None);

        // Act
        var failure = await Record.ExceptionAsync(() => hierarchy
            .InTree(s_firstTree)
            .RebuildAsync(CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
    }

    /// <summary>A cross-tree mutation rejects tracked entities from either affected tree.</summary>
    [Fact]
    public async Task CrossTreeMoveRejectsTrackedEntityFromDestinationTree()
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
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 10, CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.MoveToAsync(2, 10, CancellationToken.None));
        var firstTree = await hierarchy
            .InTree(s_firstTree)
            .Nodes
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        var secondTree = await hierarchy
            .InTree(s_secondTree)
            .Nodes
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        var rejection = Assert.IsType<NestedSetException>(error);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal([1, 2], firstTree);
        Assert.Equal([10], secondTree);
    }
}
