using MySqlConnector;
using Npgsql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>Verifies that a different scope can commit while another caller retains its tree lock.</summary>
    /// <returns>A task that completes after proving independent scope progress and rollback isolation.</returns>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason =
            "This staged contender requires independent server row locks; SQLite serializes all database writers.")]
    public async Task DifferentScopeCommitsWhileFirstCallerHoldsTreeLock()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var first = database.CreateContext();
        var probe = new EnterpriseProbe();
        await using var second = database.CreateContext((IInterceptor)probe);
        await using var transaction = await BeginCallerAsync(first);
        var firstTree = first
            .NestedSet<ConcurrentNode>()
            .ForScope(1);

        var secondTree = second
            .NestedSet<ConcurrentNode>()
            .ForScope(2);

        // WHY: This fixture reuses physical tables across cases. Fresh node keys isolate tree-registry
        // independence from InnoDB duplicate checks on recently deleted alternate-key records.
        const int firstNodeId = 1_000_001;
        const int secondNodeId = 2_000_002;
        await firstTree.InsertRootAsync(new ConcurrentNode { Id = firstNodeId }, Guid.Empty, CancellationToken.None);
        await second.Database.OpenConnectionAsync(CancellationToken.None);
        var lockTimeout = Engine switch
        {
            "PostgreSql" => "SET lock_timeout = '5s'",
            "SqlServer" => "SET LOCK_TIMEOUT 5000",
            _ => "SET SESSION innodb_lock_wait_timeout = 5",
        };

        // WHY: A server lock timeout exposes accidental cross-scope blocking, while the longer client deadline
        // allows container scheduling delays without weakening that database-level assertion.
        await second.Database.ExecuteSqlRawAsync(lockTimeout, CancellationToken.None);
        probe.Commands.Clear();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        // Act
        var error = await Record.ExceptionAsync(() => secondTree.InsertRootAsync(
            new ConcurrentNode { Id = secondNodeId },
            Guid.Empty,
            timeout.Token));

        var firstStillOwnsTransaction = ReferenceEquals(transaction, first.Database.CurrentTransaction);
        // WHY: Releasing the retained lock must complete even if the competing writer exceeded its timeout.
        await transaction.RollbackAsync(CancellationToken.None);

        // Assert
        Assert.True(
            error is null,
            $"The independent-scope write failed: {error}{Environment.NewLine}Commands:{Environment.NewLine}"
            + string.Join(Environment.NewLine, probe.Commands));
        Assert.True(firstStillOwnsTransaction);
        await using var verification = database.CreateContext();
        var persisted = await verification
            .Set<ConcurrentNode>()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(secondNodeId, persisted.Id);
        Assert.Equal(2, persisted.Tree);
        Assert.Equal((1L, 2L, 0, 0L), (persisted.Left, persisted.Right, persisted.Depth, persisted.Position));
    }

    /// <summary>A competing root reservation waits for commit before reporting the occupied TreeId.</summary>
    /// <returns>A task that completes after checking both roots and their dense sibling positions.</returns>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason =
            "This staged contender requires independent server row locks; SQLite serializes all database writers.")]
    public async Task SameTreeReservationRemainsLockedUntilCallerCommit()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var first = database.CreateContext();
        var probe = new RegistryLockProbe();
        await using var second = database.CreateContext(probe);
        await using var transaction = await BeginCallerAsync(first);
        var firstTree = first
            .NestedSet<ConcurrentNode>()
            .ForScope(1);

        var secondTree = second
            .NestedSet<ConcurrentNode>()
            .ForScope(1);

        await firstTree.InsertRootAsync(new ConcurrentNode { Id = 1 }, Guid.Empty, CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Act
        var competing = Record.ExceptionAsync(() => secondTree.InsertRootAsync(
            new ConcurrentNode { Id = 2 },
            Guid.Empty,
            timeout.Token));

        await probe.Attempted.Task.WaitAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        var error = await competing;

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, Assert.IsType<NestedSetException>(error).Code);

        await using var verification = database.CreateContext();
        var nodes = await verification
            .Set<ConcurrentNode>()
            .OrderBy(node => node.Left)
            .ToListAsync(timeout.Token);

        var node = Assert.Single(nodes);
        Assert.Equal((1, 1L, 2L, 0L), (node.Id, node.Left, node.Right, node.Position));
    }

    /// <summary>Verifies an actual database lock wait prevents a second writer from reaching structural SQL.</summary>
    /// <returns>A task that completes after the server reports a timeout while the first lock remains held.</returns>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason =
            "This staged contender requires independent server row locks; SQLite serializes all database writers.")]
    public async Task SameTreeContenderTimesOutAtRegistryBeforeReadingStructure()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var first = database.CreateContext();
        await using var transaction = await BeginCallerAsync(first);
        var firstTree = first
            .NestedSet<ConcurrentNode>()
            .ForScope(1);

        await firstTree.InsertRootAsync(new ConcurrentNode { Id = 1 }, Guid.Empty, CancellationToken.None);
        var probe = new EnterpriseProbe();
        await using var second = database.CreateContext((IInterceptor)probe);
        await second.Database.OpenConnectionAsync(CancellationToken.None);
        var configureTimeout = Engine switch
        {
            "PostgreSql" => "SET lock_timeout = '1s'",
            "SqlServer" => "SET LOCK_TIMEOUT 1000",
            _ => "SET SESSION innodb_lock_wait_timeout = 1",
        };

        // WHY: The server's lock-timeout error proves real contention; a client deadline could cancel before dispatch.
        await second.Database.ExecuteSqlRawAsync(configureTimeout, CancellationToken.None);
        probe.Commands.Clear();
        probe.ParameterValues.Clear();
        probe.ParameterNames.Clear();
        var secondTree = second
            .NestedSet<ConcurrentNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => secondTree.InsertRootAsync(
            new ConcurrentNode { Id = 2 },
            Guid.Empty,
            CancellationToken.None));

        // Assert
        if (Engine == "PostgreSql")
        {
            // WHY: Npgsql's nonretrying strategy wraps transient provider errors; the SQLSTATE proves lock contention.
            Assert.Equal("55P03", Assert.IsType<PostgresException>(exception?.GetBaseException()).SqlState);
        }
        else if (Engine == "SqlServer")
        {
            Assert.Equal(1222, Assert.IsType<SqlException>(exception?.GetBaseException()).Number);
        }
        else
        {
            Assert.Equal(1205, Assert.IsType<MySqlException>(exception).Number);
        }

        Assert.Same(transaction, first.Database.CurrentTransaction);
        Assert.Contains(probe.Commands, command => NestedSetTestInfrastructure.ReferencesRegistry(second, command));
        Assert.DoesNotContain(
            probe.Commands,
            command => !NestedSetTestInfrastructure.ReferencesRegistry(second, command)
                && command.Contains(
                    Engine switch
                    {
                        "SqlServer" => "UPDLOCK", "PostgreSql" => "FOR NO KEY UPDATE", _ => "FOR UPDATE",
                    },
                    StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            probe.Commands,
            command => command.Contains(nameof(ConcurrentNode), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            1,
            await firstTree
                .InTree(Guid.Empty)
                .Nodes
                .Select(node => node.Id)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>The first mutation creates technical tree infrastructure without a domain anchor entity.</summary>
    /// <returns>A task that completes after verifying the missing-anchor contract.</returns>
    [Fact]
    public async Task FirstMutationCreatesRegistryWithoutDomainAnchor()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<ConcurrentNode>()
            .ForScope(1);

        // Act
        await tree.InsertRootAsync(new ConcurrentNode { Id = 1 }, Guid.Empty, CancellationToken.None);

        // Assert
        var node = await context
            .Set<ConcurrentNode>()
            .SingleAsync(CancellationToken.None);

        var hierarchy = context.Model.FindEntityType(typeof(ConcurrentNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy).Registry;
        var registryCount = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .CountAsync(CancellationToken.None);

        Assert.Equal((1, 1, Guid.Empty), (node.Id, node.Tree, node.TreeId));
        Assert.Equal(1, registryCount);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Verifies invariant preservation when independent contexts write the same or different scopes.</summary>
    /// <param name="writers">The number of writers released together.</param>
    /// <param name="separateScopes">Whether every writer owns a different anchor.</param>
    /// <returns>A task that completes after all public API writes and persisted invariant checks.</returns>
    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(16, false)]
    [InlineData(16, true)]
    public async Task ConcurrentWritersPreserveScopedForestInvariants(
        int writers,
        bool separateScopes
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var pending = Enumerable
            .Range(1, writers)
            .Select(id => Task.Run(
                () => InsertAfterGateAsync(
                    database,
                    id,
                    separateScopes ? id : 1,
                    separateScopes ? Guid.Empty : TreeId(id),
                    gate.Task,
                    timeout.Token),
                timeout.Token))
            .ToArray();

        // Act
        gate.SetResult();
        await Task.WhenAll(pending);

        // Assert
        await using var verification = database.CreateContext();
        var nodes = await verification
            .Set<ConcurrentNode>()
            .OrderBy(node => node.Tree)
            .ThenBy(node => node.Id)
            .ToListAsync(timeout.Token);

        Assert.Equal(writers, nodes.Count);
        Assert.All(
            nodes,
            node =>
            {
                Assert.Null(node.ParentId);
                Assert.Equal(0, node.Depth);
                Assert.Equal((1L, 2L, 0L), (node.Left, node.Right, node.Position));
            });
        Assert.Equal(
            separateScopes ? writers : 1,
            nodes
                .Select(node => node.Tree)
                .Distinct()
                .Count());
        Assert.Equal(
            writers,
            nodes
                .Select(node => (node.Tree, node.TreeId))
                .Distinct()
                .Count());
    }

    /// <summary>Runs one public hierarchy insertion on a context owned exclusively by that writer.</summary>
    private static async Task InsertAfterGateAsync(
        TestDatabase database,
        int id,
        int scope,
        Guid treeId,
        Task gate,
        CancellationToken cancellationToken
    )
    {
        await gate.WaitAsync(cancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<ConcurrentNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(new ConcurrentNode { Id = id }, treeId, cancellationToken);
    }

    /// <summary>Creates a deterministic nonempty TreeId for one independently written test tree.</summary>
    private static Guid TreeId(
        int value
    ) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
