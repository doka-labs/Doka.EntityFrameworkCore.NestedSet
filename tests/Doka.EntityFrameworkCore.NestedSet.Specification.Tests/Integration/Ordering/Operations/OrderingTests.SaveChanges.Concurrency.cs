namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Rejects a stale tree assignment before a sorted payload can write in an unlocked tree.</summary>
    [EngineFact(
        ExcludedEngines = new[] { "Sqlite" },
        Reason = "This staged contender requires independent server row locks; SQLite serializes all database writers."
    )]
    public async Task SortedSaveRejectsNodeMovedToUnlockedTree()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var hierarchy = setup.NestedSet<OrderingNode>().ForScope(1);
        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await hierarchy.InsertRootAsync(Node(10, "Destination"), secondTree, CancellationToken.None);
        await using var barrier = new SaveRegistryLockBarrier();
        await using var first = await _fixture.CreateContextAsync(Engine, "Strict", barrier);
        await using var second = await _fixture.CreateContextAsync(Engine);
        var renamed = await first.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        renamed.Name = "Zulu";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act
        var pendingSave = first.SaveChangesAsync(timeout.Token);
        await barrier.Attempted.Task.WaitAsync(timeout.Token);

        try
        {
            await second.NestedSet<OrderingNode>().ForScope(1)
                .MoveToAsync(2, 10, timeout.Token);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        var failure = await Record.ExceptionAsync(() => pendingSave);

        // Assert
        var conflict = Assert.IsType<DbUpdateConcurrencyException>(failure);
        Assert.Contains("changed trees before locking", conflict.Message, StringComparison.Ordinal);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var node = await verification.Set<OrderingNode>()
            .AsNoTracking()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);
        Assert.Equal(secondTree, node.TreeId);
        Assert.Equal("Alpha", node.Name);
        var scope = verification.NestedSet<OrderingNode>().ForScope(1);
        var firstReport = await scope.InTree(firstTree)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        var secondReport = await scope.InTree(secondTree)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        Assert.True(firstReport.IsValid);
        Assert.True(secondReport.IsValid);
    }

    /// <summary>Pauses the first existing-tree registry lock after the save's unlocked identity lookup.</summary>
    private sealed class SaveRegistryLockBarrier : DbCommandInterceptor, IAsyncDisposable
    {
        private int _observed;

        /// <summary>Signals that the save reached its first existing-tree lock.</summary>
        internal TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Allows the intercepted lock to continue.</summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DokaNestedSetTrees_", StringComparison.Ordinal)
                && (command.CommandText.Contains("UPDLOCK", StringComparison.Ordinal)
                    || command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                    || command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
                && Interlocked.Exchange(ref _observed, 1) == 0)
            {
                Attempted.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Release.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }
}
