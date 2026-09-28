namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingRefreshScaleTests
{
    /// <summary>
    ///     Disposes an interrupted refresh reader before rollback and restores the pre-save tracker snapshot.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedStreamingRefreshClosesReaderBeforeRollback(
        bool cancel
    )
    {
        // Arrange
        await OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, 3);
        using var probe = new ScaleProbe();
        var rollback = new RefreshRollbackProbe(probe);
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe, rollback);
        var tracked = await context
            .Set<OrderingNode>()
            .Where(node => node.ParentId != null)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var original = tracked
            .Select(node => (node.Left, node.Right, node.Position))
            .ToArray();

        tracked[0].Name = "zzzz";
        using var cancellation = new CancellationTokenSource();
        var injected = new OrderingInjectedException();
        var interrupted = false;
        var readersAtInterruption = 0;
        context.ChangeTracker.StateChanged += (_, arguments) =>
        {
            if (!interrupted
                && ReferenceEquals(arguments.Entry.Entity, tracked[1])
                && arguments.NewState == EntityState.Modified)
            {
                interrupted = true;
                readersAtInterruption = probe.ActiveReaders;

                if (cancel)
                {
                    // WHY: This synchronous state callback must expose cancellation before the next reader step.
                    // ReSharper disable once MethodHasAsyncOverload
                    cancellation.Cancel();
                }
                else
                {
                    throw injected;
                }
            }
        };
        probe.Reset();

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(false, cancellation.Token));

        // Assert
        Assert.True(interrupted);
        Assert.Equal(1, readersAtInterruption);
        Assert.Equal(1, rollback.Attempts);
        Assert.Equal(0, rollback.ReadersAtRollback);
        Assert.Equal(0, probe.ActiveReaders);

        if (cancel)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
        }
        else
        {
            Assert.Same(injected, failure);
        }

        Assert.Equal(original, tracked.Select(node => (node.Left, node.Right, node.Position)));
        Assert.True(
            context
                .Entry(tracked[0])
                .Property(node => node.Name)
                .IsModified);
        Assert.Equal("zzzz", tracked[0].Name);
        Assert.Equal(
            "node-00000001",
            context
                .Entry(tracked[0])
                .Property(node => node.Name)
                .OriginalValue);
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var persisted = await verification
            .Set<OrderingNode>()
            .AsNoTracking()
            .Where(node => node.ParentId != null)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(original, persisted.Select(node => (node.Left, node.Right, node.Position)));
        Assert.Equal("node-00000001", persisted[0].Name);
    }

    /// <summary>Records the real active-reader count immediately before the provider rollback call.</summary>
    private sealed class RefreshRollbackProbe : DbTransactionInterceptor
    {
        private readonly ScaleProbe _commands;

        /// <summary>Observes the same connection's command lifecycle without changing transaction execution.</summary>
        internal RefreshRollbackProbe(
            ScaleProbe commands
        )
        {
            _commands = commands;
        }

        /// <summary>Gets rollback attempts after the actual mid-refresh interruption.</summary>
        internal int Attempts { get; private set; }

        /// <summary>Gets the maximum number of live readers at the rollback boundary.</summary>
        internal int ReadersAtRollback { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Attempts++;
            ReadersAtRollback = Math.Max(ReadersAtRollback, _commands.ActiveReaders);

            return ValueTask.FromResult(result);
        }
    }
}
