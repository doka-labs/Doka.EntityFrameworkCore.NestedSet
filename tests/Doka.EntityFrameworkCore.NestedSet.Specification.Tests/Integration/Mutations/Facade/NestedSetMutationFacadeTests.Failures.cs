namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetMutationFacadeTests
{
    /// <summary>DeleteAsync rejects a root without changing the tree.</summary>
    [Fact]
    public async Task DeleteRejectsRoot()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => hierarchy.DeleteAsync(
            1,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.OperationRejected, error.Code);
        Assert.Equal(
            1,
            await context
                .Set<TreeNode>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>A pre-canceled root insertion writes neither a registry identity nor a node.</summary>
    [Fact]
    public async Task PreCanceledRootInsertionWritesNothing()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Empty(
            await context
                .Set<TreeNode>()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>A missing insertion anchor is reported before any structural row changes.</summary>
    [Fact]
    public async Task MissingInsertionAnchorWritesNothing()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => hierarchy.InsertChildAsync(
            new TreeNode { NodeId = 2 },
            999,
            CancellationToken.None));

        var nodeIds = await context
            .Set<TreeNode>()
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.NodeNotFound, error.Code);
        Assert.Equal([1], nodeIds);
    }

    /// <summary>A subtree cannot move relative to itself or beneath one of its descendants.</summary>
    [Fact]
    public async Task CyclicMoveWritesNothing()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        var before = await StructureAsync(context);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => hierarchy.MoveToAsync(
            1,
            2,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.CycleDetected, error.Code);
        Assert.Equal(before, await StructureAsync(context));
    }

    /// <summary>Relative sibling moves reject a destination in another tree before changing either tree.</summary>
    [Fact]
    public async Task RelativeMoveRejectsAnotherTree()
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
        var before = await StructureAsync(context);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.MoveBeforeAsync(2, 10, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.OperationRejected, error.Code);
        Assert.Equal(before, await StructureAsync(context));
    }

    /// <summary>Detach rejects both active and retired destination identities before moving any row.</summary>
    [Fact]
    public async Task DetachRejectsUnavailableTreeId()
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
        await hierarchy.DeleteTreeAsync(s_secondTree, CancellationToken.None);
        var before = await StructureAsync(context);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.DetachAsTreeAsync(2, s_secondTree, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, error.Code);
        Assert.Equal(before, await StructureAsync(context));
    }

    /// <summary>Concurrent first writers reserve one TreeId exactly once.</summary>
    [Fact]
    public async Task ConcurrentDuplicateRootsHaveOneWinner()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstHierarchy = first
            .NestedSet<TreeNode>()
            .ForScope(7);

        var secondHierarchy = second
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        var outcomes = await Task.WhenAll(
            CaptureAsync(() => firstHierarchy.InsertRootAsync(
                new TreeNode { NodeId = 1 },
                s_firstTree,
                CancellationToken.None)),
            CaptureAsync(() => secondHierarchy.InsertRootAsync(
                new TreeNode { NodeId = 2 },
                s_firstTree,
                CancellationToken.None)));

        // Assert
        Assert.Single(outcomes, error => error is null);
        var failure = Assert.Single(outcomes, error => error is not null);
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, Assert.IsType<NestedSetException>(failure).Code);
        await using var verification = database.CreateContext();
        Assert.Equal(
            1,
            await verification
                .Set<TreeNode>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Captures one concurrent operation outcome without obscuring its original exception.</summary>
    private static async Task<Exception?> CaptureAsync(
        Func<Task> operation
    )
    {
        try
        {
            await operation();

            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    /// <summary>Reads only structural values in stable key order for rollback assertions.</summary>
    private static Task<(int Id, Guid TreeId, int? Parent, long Left, long Right, int Depth, long Position)[]>
        StructureAsync(
            TreeContext context
        ) => context
        .Set<TreeNode>()
        .AsNoTracking()
        .OrderBy(node => node.NodeId)
        .Select(node => new ValueTuple<int, Guid, int?, long, long, int, long>(
            node.NodeId,
            node.TreeId,
            node.Parent,
            node.Start,
            node.End,
            node.Depth,
            node.Position))
        .ToArrayAsync(CancellationToken.None);
}
