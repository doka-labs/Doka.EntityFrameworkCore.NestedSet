namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>An invalid target is rejected without changing the forest.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveIntoDescendantIsRejected()
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
        var error = await Record.ExceptionAsync(() => tree.MoveToAsync(1, 2, CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>An invalid target is rejected without changing the forest.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveToMissingSiblingIsRejected()
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
        var error = await Record.ExceptionAsync(() => tree.MoveAfterAsync(1, 99, CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>A sibling in another scope cannot be used as a mutation target.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveToSiblingFromAnotherScopeIsRejected()
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
        var isolatedBefore = await SnapshotAsync(context, 2);
        var before = await SnapshotAsync(context, 1);

        // Act
        var error = await Record.ExceptionAsync(() => tree.MoveAfterAsync(1, 99, CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        Assert.Equal(isolatedBefore, await SnapshotAsync(context, 2));
        await AssertValidAsync(context, 1);
        await AssertValidAsync(context, 2);
    }

    /// <summary>An invalid target is rejected without changing the forest.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DeleteMissingNodeIsRejected()
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
        var error = await Record.ExceptionAsync(() => tree.DeleteAsync(-1, CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>A failed insert restores assigned structure and the caller-owned entity.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DuplicateKeyRestoresDatabaseAndDetachedEntity()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);
        var before = await SnapshotAsync(context, 1);
        var duplicate = new TreeNode
        {
            NodeId = 1,
            Start = 17,
            End = 20,
            Tree = 88,
            Depth = 9,
            Position = 7,
        };

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertBeforeAsync(duplicate, 2, CancellationToken.None));

        // Assert
        Assert.IsType<DbUpdateException>(error);
        Assert.Equal(
            (17, 20, 88, 9, 7),
            (duplicate.Start, duplicate.End, duplicate.Tree, duplicate.Depth, duplicate.Position));
        Assert.Equal(EntityState.Detached, context.Entry(duplicate).State);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>Cancellation before mutation leaves all persisted structure unchanged.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task CanceledDeleteLeavesForestUnchanged()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);
        var before = await SnapshotAsync(context, 1);
        var cancellationToken = new CancellationToken(canceled: true);

        // Act
        var error = await Record.ExceptionAsync(() => tree.DeleteSubtreeAsync(1, cancellationToken));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>Failure after range writes rolls the entire mutation back.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task CommandFailureRollsBackWrittenBounds()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);
        var before = await SnapshotAsync(context, 1);
        probe.Reset(failAfterBoundsUpdate: true);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertAsFirstChildAsync(Node(7), 1, CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        Assert.Equal(1, probe.BoundsUpdatesCompleted);
        Assert.True(probe.BoundsRowsAffected > 0);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>Cancellation during range writes rolls the entire mutation back.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MidWriteCancellationRollsBackWrittenBounds()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);
        var before = await SnapshotAsync(context, 1);
        using var cancellation = new CancellationTokenSource();
        probe.Reset();
        probe.CancelAfterBoundsUpdate = cancellation;

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertAsFirstChildAsync(Node(7), 1, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(1, probe.BoundsUpdatesCompleted);
        Assert.True(probe.BoundsRowsAffected > 0);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>A failure after the database changed depth still rolls every structural value back.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveFailureRollsBackExecutedDepthUpdate()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDepthFailureTreeAsync(context, 1);
        var before = await SnapshotAsync(context, 1);
        probe.Reset(failAfterDepthUpdate: true);

        // Act
        var error = await Record.ExceptionAsync(() => tree.MoveToAsync(2, 7, CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        Assert.Equal(1, probe.DepthUpdatesCompleted);

        // WHY: Crossing TreeIds rewrites exactly the three moved rows; unrelated destination rows keep their depth.
        Assert.Equal(3, probe.DepthStatementRowsAffected);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>A failure after the database changed depth still rolls every structural value back.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DeleteFailureRollsBackExecutedDepthUpdate()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDepthFailureTreeAsync(context, 1);
        var before = await SnapshotAsync(context, 1);
        probe.Reset(failAfterDepthUpdate: true);

        // Act
        var error = await Record.ExceptionAsync(() => tree.DeleteAsync(2, CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        Assert.Equal(1, probe.DepthUpdatesCompleted);

        // WHY: Promotion updates the three surviving rows in the selected tree, excluding the unrelated tree.
        Assert.Equal(3, probe.DepthStatementRowsAffected);
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>The insert UPDATE count is independent of sibling-group size.</summary>
    /// <param name="existingChildren">The sibling-group size before the measured insertion.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(18)]
    [InlineData(36)]
    public async Task InsertUsesConstantSetBasedUpdateCount(
        int existingChildren
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        for (var id = 2; id < existingChildren + 2; id++)
        {
            await tree.InsertChildAsync(Node(id), 1, cancellationToken: CancellationToken.None);
        }

        var smallTree = context
            .NestedSet<TreeNode>()
            .ForScope(2);

        await smallTree.InsertRootAsync(Node(1000), Guid.Empty, CancellationToken.None);
        await smallTree.InsertChildAsync(Node(1001), 1000, cancellationToken: CancellationToken.None);
        probe.Reset();
        await smallTree.InsertAsFirstChildAsync(Node(1002), 1000, CancellationToken.None);
        var baselineCount = probe.UpdateCount;
        probe.Reset();

        // Act
        await tree.InsertAsFirstChildAsync(Node(100), 1, CancellationToken.None);

        // Assert
        Assert.Equal(baselineCount, probe.UpdateCount);
        Assert.InRange(probe.UpdateCount, 1, 12);
        await AssertValidAsync(context, 1);
        await AssertValidAsync(context, 2);
    }

    /// <summary>The derived database constraint rejects a negative depth without changing the tree.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DatabaseConstraintRejectsNegativeDepthWithoutPartialWrites()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1);
        var before = await SnapshotAsync(context, 1);

        // Act
        var error = await Record.ExceptionAsync(() => context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .Where(node => node.NodeId == 2)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Depth, -1), CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<DbException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
    }

    /// <summary>Moving a subtree rejects descendant depth overflow without partial writes.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DescendantDepthOverflowRejectsMove()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDepthFailureTreeAsync(context, 1);
        await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .Where(x => x.NodeId == 4)
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.Depth, int.MaxValue), CancellationToken.None);

        var before = await SnapshotAsync(context, 1);

        // Act
        var error = await Record.ExceptionAsync(() => tree.MoveToAsync(2, 7, CancellationToken.None));

        // Assert
        Assert.IsType<OverflowException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
    }

    /// <summary>Inserting below the maximum depth cannot wrap the child depth.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ParentDepthOverflowRejectsInsert()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1);
        await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .Where(x => x.NodeId == 8)
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.Depth, int.MaxValue), CancellationToken.None);

        var before = await SnapshotAsync(context, 1);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertChildAsync(
            Node(9),
            8,
            cancellationToken: CancellationToken.None));

        // Assert
        Assert.IsType<OverflowException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
    }

    /// <summary>The derived database constraint rejects a negative boundary without changing the tree.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DatabaseConstraintRejectsNegativeBoundaryWithoutPartialWrites()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        var before = await SnapshotAsync(context, 1);

        // Act
        var error = await Record.ExceptionAsync(() => context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Start, -1), CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<DbException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
    }

    /// <summary>A full coordinate range is rejected instead of wrapping boundaries.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task BoundaryOverflowRejectsInsert()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.End, long.MaxValue - 1),
                CancellationToken.None);

        var before = await SnapshotAsync(context, 1);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertChildAsync(Node(2), 1, CancellationToken.None));

        // Assert
        Assert.IsType<OverflowException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
    }
}
