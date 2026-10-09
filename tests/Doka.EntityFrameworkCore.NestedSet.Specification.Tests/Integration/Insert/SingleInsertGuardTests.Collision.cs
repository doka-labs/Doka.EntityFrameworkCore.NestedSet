namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>A callback identity collision retains the original EF error without claiming cleanup failed.</summary>
    /// <param name="callerTransaction">Whether the rejected insert must preserve a caller-owned transaction.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleIdentityCollisionPreservesLegitimateTrackedRowAndReleasesInput(
        bool callerTransaction
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await using var transaction = callerTransaction
            ? await context.Database.BeginTransactionAsync(
                Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                CancellationToken.None)
            : null;

        var marker = new UnrelatedRow { Id = 701 };
        var legitimate = NewAssignedSingleScalarInput(Guid.NewGuid());
        legitimate.NodeId = 102;
        legitimate.Tree = 1;
        legitimate.Parent = null;
        legitimate.Start = 1;
        legitimate.End = 2;
        legitimate.Depth = 0;
        legitimate.Position = 0;
        await context.AddAsync(marker, CancellationToken.None);
        await context.AddAsync(legitimate, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);

        // Act
        var rejected = await FailSingleIdentityCollisionAsync(context);

        // WHY: Async completion can resume this observer inside the producer's exception stack. Yield once
        // so those active frames finish before measuring references held by the live context.
        await Task.Yield();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        var retained = rejected.Input.IsAlive;

        // Assert
        Assert.True(rejected.Restored);
        Assert.False(retained);
        Assert.Equal(
            2,
            context
                .ChangeTracker
                .Entries()
                .Count());
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(marker)
                .State);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(legitimate)
                .State);
        Assert.Same(legitimate, await context.FindAsync<TreeNode>([102], CancellationToken.None));
        Assert.Null(await context.FindAsync<TreeNode>([101], CancellationToken.None));
        var saved = Assert.Single(
            await context
                .Set<TreeNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal((102, legitimate.TreeId, 1L, 2L), (saved.NodeId, saved.TreeId, saved.Start, saved.End));
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.True(rejected.OriginalError);
        GC.KeepAlive(context);
    }

    /// <summary>Releases test callbacks and strong input references after the failed scalar collision.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Input, bool OriginalError, bool Restored)>
        FailSingleIdentityCollisionAsync(
            DbContext context
        )
    {
        var treeId = Guid.NewGuid();
        var input = NewAssignedSingleScalarInput(treeId);
        var reference = new WeakReference(input);
        EventHandler<SavingChangesEventArgs> mutate = (
            _,
            _
        ) =>
        {
            if (input is not null)
            {
                input.NodeId = 102;
            }
        };
        context.SavingChanges += mutate;
        Exception? error;

        try
        {
            error = await Record.ExceptionAsync(() =>
            {
                ArgumentNullException.ThrowIfNull(input);

                return context
                    .NestedSet<TreeNode>()
                    .ForScope(1)
                    .InsertRootAsync(input, Guid.Empty, CancellationToken.None);
            });
        }
        finally
        {
            context.SavingChanges -= mutate;
        }

        var restored = (input.NodeId, input.Tree, input.TreeId, input.Parent, input.Start, input.End,
                input.Depth, input.Position)
            == (101, 9, treeId, 8, 71L, 72L, 3, 4L);
        var originalError = error?.GetType() == typeof(InvalidOperationException);

        // WHY: Retaining the exception or callback capture can retain a completed asynchronous producer.
        // Preserve the exact error classification as a value and release the test's own input before collection.
        input = null;
        error = null;

        return (reference, originalError, restored);
    }
}
