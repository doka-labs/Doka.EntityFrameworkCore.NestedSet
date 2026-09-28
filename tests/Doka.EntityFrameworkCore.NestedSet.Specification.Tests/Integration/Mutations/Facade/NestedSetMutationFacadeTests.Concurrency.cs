namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetMutationFacadeTests
{
    /// <summary>Reports a concurrent non-root tree change as a stale identity, not a missing node.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "This staged contender requires independent server row locks; SQLite serializes all database writers."
    )]
    public async Task ConcurrentTreeChangeHasDistinctErrorCode()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using (var setup = database.CreateContext())
        {
            var hierarchy = setup
                .NestedSet<TreeNode>()
                .ForScope(7);

            await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
            await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
            await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);
        }

        var barrier = new ExistingRegistryLockBarrier();
        await using var first = database.CreateContext(barrier);
        await using var second = database.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act
        var pendingDelete = first
            .NestedSet<TreeNode>()
            .ForScope(7)
            .DeleteAsync(2, timeout.Token);

        await barrier.Attempted.Task.WaitAsync(timeout.Token);

        try
        {
            await second
                .NestedSet<TreeNode>()
                .ForScope(7)
                .MoveToAsync(2, 10, timeout.Token);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        var failure = await Record.ExceptionAsync(() => pendingDelete);

        // Assert
        var conflict = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.ConcurrentTreeIdentity, conflict.Code);
        await using var verification = database.CreateContext();
        Assert.Equal(
            s_secondTree,
            await verification
                .Set<TreeNode>()
                .Where(node => node.NodeId == 2)
                .Select(node => node.TreeId)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>A tombstoned former root also reports the intervening tree move.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "This staged contender requires independent server row locks; SQLite serializes all database writers."
    )]
    public async Task ConcurrentRootMoveDoesNotReportTreeIdUnavailable()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using (var setup = database.CreateContext())
        {
            var hierarchy = setup
                .NestedSet<TreeNode>()
                .ForScope(7);

            await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
            await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);
        }

        var barrier = new ExistingRegistryLockBarrier();
        await using var first = database.CreateContext(barrier);
        await using var second = database.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act
        var pendingDelete = first
            .NestedSet<TreeNode>()
            .ForScope(7)
            .DeleteSubtreeAsync(1, timeout.Token);

        await barrier.Attempted.Task.WaitAsync(timeout.Token);

        try
        {
            await second
                .NestedSet<TreeNode>()
                .ForScope(7)
                .MoveToAsync(1, 10, timeout.Token);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        var failure = await Record.ExceptionAsync(() => pendingDelete);

        // Assert
        var conflict = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.ConcurrentTreeIdentity, conflict.Code);
        await using var verification = database.CreateContext();
        Assert.Equal(
            s_secondTree,
            await verification
                .Set<TreeNode>()
                .Where(node => node.NodeId == 1)
                .Select(node => node.TreeId)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Opposing cross-tree writers complete under the same deterministic registry-lock order.</summary>
    [Fact]
    public async Task OpposingCrossTreeMovesDoNotDeadlock()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using (var setup = database.CreateContext())
        {
            var hierarchy = setup
                .NestedSet<TreeNode>()
                .ForScope(7);

            await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
            await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
            await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);
            await hierarchy.InsertChildAsync(new TreeNode { NodeId = 20 }, 10, CancellationToken.None);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act
        await Task.WhenAll(
            first
                .NestedSet<TreeNode>()
                .ForScope(7)
                .MoveToAsync(2, 10, timeout.Token),
            second
                .NestedSet<TreeNode>()
                .ForScope(7)
                .MoveToAsync(20, 1, timeout.Token));

        await using var verification = database.CreateContext();
        var hierarchyAfter = verification
            .NestedSet<TreeNode>()
            .ForScope(7);

        var firstTree = await hierarchyAfter
            .InTree(s_firstTree)
            .Nodes
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        var secondTree = await hierarchyAfter
            .InTree(s_secondTree)
            .Nodes
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 20], firstTree);
        Assert.Equal([10, 2], secondTree);
    }

    /// <summary>Pauses the first existing-tree lock after facade anchor resolution.</summary>
    private sealed class ExistingRegistryLockBarrier : DbCommandInterceptor
    {
        private int _observed;

        /// <summary>Signals that the pre-lock facade lookup completed.</summary>
        internal TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Allows the originally resolved tree lock to continue.</summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
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
    }
}
