namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>Partial root and owned tracking failures release hidden references in a live context.</summary>
    /// <param name="owned">Whether the failure occurs while tracking the owned payload.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedSingleTrackingReleasesInputReferences(
        bool owned
    )
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var rejected = await FailSingleTrackingInputAsync(context, owned);

        // WHY: Async completion can resume this observer inside the producer's exception stack. Yield once
        // so those active frames finish before measuring references held by the live context.
        await Task.Yield();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Assert
        Assert.True(rejected.Restored);
        Assert.False(rejected.Root.IsAlive);
        Assert.False(rejected.Owned.IsAlive);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Empty(
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Ends every caller-owned strong reference before the live-context retention observation.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Root, WeakReference Owned, bool Restored)> FailSingleTrackingInputAsync(
        SingleIdentityContext context,
        bool owned
    )
    {
        var treeId = Guid.NewGuid();
        var input = NewCustomInput(treeId);
        var rootReference = new WeakReference(input);
        var ownedReference = new WeakReference(input.Details);
        var failure = new InvalidOperationException("Injected single insertion reference-retention probe.");
        EventHandler<EntityTrackingEventArgs> callback = (_, args) =>
        {
            if (input is not null
                && ReferenceEquals(args.Entry.Entity, owned ? input.Details : input))
            {
                throw failure;
            }
        };

        context.ChangeTracker.Tracking += callback;
        Exception? error;

        try
        {
            error = await Record.ExceptionAsync(() =>
            {
                ArgumentNullException.ThrowIfNull(input);

                return context
                    .NestedSet<SingleIdentityNode>()
                    .InsertRootAsync(input, Guid.Empty, CancellationToken.None);
            });
        }
        finally
        {
            // WHY: The probe must measure EF's native storage rather than its own callback closure.
            context.ChangeTracker.Tracking -= callback;
        }

        var restored = ReferenceEquals(error, failure)
            && (input.Id.Value, input.TreeId, input.ParentId?.Value, input.Left, input.Right, input.Depth,
                input.Position)
            == (3, treeId, 8, 71, 72, 3, 4);

        // WHY: An asynchronous provider can resume the observer before this producing frame unwinds.
        // Clear the test callback's captured input so collection measures only the live context's references.
        input = null;
        error = null;

        return (rootReference, ownedReference, restored);
    }
}
