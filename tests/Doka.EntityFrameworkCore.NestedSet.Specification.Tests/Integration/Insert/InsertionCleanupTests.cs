namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that insertion cleanup preserves failures and continues through owned entities.</summary>
public abstract partial class InsertionCleanupTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the existing provider fixture without introducing another EF service graph.</summary>
    protected InsertionCleanupTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Retains the original operation or cancellation failure when a detach callback also fails.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedInsertionPreservesOperationAndDetachErrors(
        bool bulk,
        bool cancel
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        using var cancellation = new CancellationTokenSource();
        var probe = new InsertionFailureProbe(cancel ? cancellation : null);
        await using var context = database.CreateContext(probe);
        var marker = new UnrelatedRow { Id = 701 };
        context.Attach(marker);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var root = new TreeNode
        {
            NodeId = 101,
            Tree = 9,
            Start = 71,
            End = 72,
            Depth = 3,
            Position = 4,
        };

        var child = new TreeNode
        {
            NodeId = 102,
            Tree = 8,
            Start = 81,
            End = 82,
            Depth = 5,
            Position = 6,
        };

        var cleanup = new InsertionCleanupException();
        var detachCallbacks = 0;
        context.ChangeTracker.StateChanged += (_, args) =>
        {
            if (probe.Saved
                && ReferenceEquals(args.Entry.Entity, root)
                && args.NewState == EntityState.Detached)
            {
                detachCallbacks++;

                throw cleanup;
            }
        };

        // Act
        var error = await Record.ExceptionAsync(() => bulk
            ? tree.InsertForestAsync(
                [
                    new NestedSetTreeImport<TreeNode, Guid>(
                        Guid.Empty,
                        new NestedSetBranch<TreeNode>(root, [new NestedSetBranch<TreeNode>(child)])),
                ],
                cancellation.Token)
            : tree.InsertRootAsync(root, Guid.Empty, cancellation.Token));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(error)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(probe.Failure, failures);
        Assert.Contains(cleanup, failures);
        Assert.True(probe.Saved);
        Assert.Equal(1, detachCallbacks);
        Assert.Equal((9, 71, 72, 3, 4), (root.Tree, root.Start, root.End, root.Depth, root.Position));
        Assert.Equal((8, 81, 82, 5, 6), (child.Tree, child.Start, child.End, child.Depth, child.Position));
        Assert.Equal(EntityState.Detached, context.Entry(root).State);
        Assert.Equal(EntityState.Detached, context.Entry(child).State);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Set<TreeNode>().ToArrayAsync(CancellationToken.None));
    }

    /// <summary>A pre-detach callback failure aborts the bulk wave and restores every owned input.</summary>
    [Fact]
    public async Task BulkWaveDetachFailureRollsBackAndRestoresAllInputs()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var root = new TreeNode
        {
            NodeId = 101,
            Tree = 9,
            Start = 71,
            End = 72,
            Depth = 3,
            Position = 4,
        };

        var child = new TreeNode
        {
            NodeId = 102,
            Tree = 8,
            Start = 81,
            End = 82,
            Depth = 5,
            Position = 6,
        };

        var branch = new NestedSetBranch<TreeNode>(root, [new NestedSetBranch<TreeNode>(child)]);
        var failure = new InsertionCleanupException();
        var intercepted = false;
        context.ChangeTracker.StateChanging += (_, args) =>
        {
            if (!intercepted
                && ReferenceEquals(args.Entry.Entity, root)
                && args.NewState == EntityState.Detached)
            {
                // WHY: Throwing before the transition leaves a live entry which rollback must still own and restore.
                intercepted = true;

                throw failure;
            }
        };

        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<TreeNode, Guid>(Guid.NewGuid(), branch)],
            CancellationToken.None));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(error)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(failure, failures);
        Assert.True(intercepted);
        Assert.Equal((9, 71, 72, 3, 4), (root.Tree, root.Start, root.End, root.Depth, root.Position));
        Assert.Equal((8, 81, 82, 5, 6), (child.Tree, child.Start, child.End, child.Depth, child.Position));
        Assert.Equal(Guid.Empty, root.TreeId);
        Assert.Equal(Guid.Empty, child.TreeId);
        Assert.Equal(EntityState.Detached, context.Entry(root).State);
        Assert.Equal(EntityState.Detached, context.Entry(child).State);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Set<TreeNode>().ToArrayAsync(CancellationToken.None));
    }
}
