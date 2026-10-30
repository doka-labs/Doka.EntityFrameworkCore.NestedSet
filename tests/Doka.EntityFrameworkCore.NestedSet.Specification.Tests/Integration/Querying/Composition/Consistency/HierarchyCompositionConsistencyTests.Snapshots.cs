using Microsoft.Data.Sqlite;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionConsistencyTests
{
    /// <summary>A real concurrent commit cannot mix old ancestry with new periods or joined payload.</summary>
    /// <param name="rebuild">Whether the writer rebuilds changed adjacency instead of using the move API.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedQueryReadsOneQualifiedSnapshotAcrossAtomicStructureAndPayloadChanges(
        bool rebuild
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var setup = await CreateContextAsync(scope, probe);
        await using var writer = await _fixture.CreateContextAsync<CompositionContext>(
            Engine,
            static options => new CompositionContext(options));

        var committedDuringRead = false;
        var boundary = new CompositionReadBoundaryProbe(async token =>
        {
            await ChangeStructureAndPayloadAsync(writer, scope, rebuild, token);
            committedDuringRead = true;
        });

        await using var context = await _fixture.CreateContextAsync<CompositionContext>(
            Engine,
            static options => new CompositionContext(options),
            probe,
            boundary);

        await ConfigureSnapshotAsync(context);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var native = Engine == "Sqlite" ? BeginDeferredSqliteTransaction(context) : null;
        await using var transaction = native is null
            ? await context.Database.BeginTransactionAsync(
                Engine == "SqlServer" ? IsolationLevel.Snapshot : IsolationLevel.RepeatableRead,
                CancellationToken.None)
            : await context.Database.UseTransactionAsync(native, CancellationToken.None)
            ?? throw new InvalidOperationException("The SQLite snapshot transaction must be enlisted in EF.");

        var query = BuildMixedQuery(context, scope);
        probe.Reset();
        boundary.Armed = true;

        // Act
        var before = await query.ToArrayAsync(CancellationToken.None);
        var preservedTransaction = ReferenceEquals(transaction, context.Database.CurrentTransaction);
        var actualIsolation = transaction.GetDbTransaction()
            .IsolationLevel;
        boundary.Armed = false;
        await transaction.RollbackAsync(CancellationToken.None);
        await using var verification = await _fixture.CreateContextAsync<CompositionContext>(
            Engine,
            static options => new CompositionContext(options));

        var after = await BuildMixedQuery(verification, scope)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.True(committedDuringRead);
        Assert.True(preservedTransaction);
        Assert.Equal(
            Engine switch
            {
                "Sqlite" => IsolationLevel.Serializable,
                "SqlServer" => IsolationLevel.Snapshot,
                _ => IsolationLevel.RepeatableRead,
            },
            actualIsolation);

        Assert.Equal(1, boundary.FirstRows);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(ExpectedSnapshot(afterChange: false), before);
        Assert.Equal(ExpectedSnapshot(afterChange: true), after);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(verification.ChangeTracker.Entries());
    }

    /// <summary>
    ///     Enables only test-owned provider options required for a nonblocking snapshot-versus-writer proof.
    /// </summary>
    private async Task ConfigureSnapshotAsync(
        CompositionContext context
    )
    {
        if (Engine == "SqlServer")
        {
            // WHY: Locking READ COMMITTED is not a snapshot. Only this test fixture opts into SNAPSHOT;
            // the query library must neither alter database options nor silently change caller isolation.
            await context.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON",
                CancellationToken.None);
        }
        else if (Engine == "Sqlite")
        {
            // WHY: WAL permits the competing commit while the reader retains its original version.
            // Rollback journaling would block that writer and make an await-inside-reader barrier deadlock.
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL", CancellationToken.None);
        }
    }

    /// <summary>Creates SQLite's real deferred boundary without reserving the writer lock at BEGIN.</summary>
    private static SqliteTransaction BeginDeferredSqliteTransaction(
        CompositionContext context
    )
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        // WHY: Microsoft.Data.Sqlite exposes deferred transaction selection only on this synchronous overload.
        // The async inherited overload begins IMMEDIATE and would block the adversarial writer at setup.
        // This exception is limited to test setup; the composed query still uses an actual async reader.
        // ReSharper disable once MethodHasAsyncOverload
        return connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
    }

    /// <summary>Commits hierarchy, temporal state and an independent payload table as one application unit.</summary>
    private static async Task ChangeStructureAndPayloadAsync(
        CompositionContext writer,
        Guid scope,
        bool rebuild,
        CancellationToken cancellationToken
    )
    {
        // WHY: Structural writers require READ COMMITTED on server engines independently of the reader's
        // stable snapshot. InnoDB's connection default is REPEATABLE READ; SQLite uses its serializable writer.
        await using var transaction = await writer.Database.BeginTransactionAsync(
            writer.Database.IsSqlite() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            cancellationToken);

        var hierarchy = writer
            .NestedSet<CompositionNode>()
            .ForScope(scope);

        if (rebuild)
        {
            var treeId = await writer
                .Set<CompositionNode>()
                .Where(node => node.Scope == scope && node.Id == 1)
                .Select(node => node.TreeId)
                .SingleAsync(cancellationToken);

            await writer
                .Set<CompositionNode>()
                .Where(node => node.Scope == scope && node.Id == 3)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(node => node.ParentId, 5)
                        .SetProperty(node => node.Position, 1),
                    cancellationToken);

            await hierarchy
                .InTree(treeId)
                .RebuildAsync(cancellationToken);
        }
        else
        {
            await hierarchy.MoveToAsync(3, 5, cancellationToken);
        }

        await writer
            .Set<CompositionNode>()
            .Where(node => node.Scope == scope && node.Id == 3)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(node => node.Matches, true)
                    .SetProperty(node => node.FromDate, new DateOnly(2044, 9, 1))
                    .SetProperty(node => node.ToDate, new DateOnly(2044, 10, 31)),
                cancellationToken);

        await writer
            .Set<CompositionInputOne>()
            .Where(fact => fact.Scope == scope && fact.NodeId == 3)
            .ExecuteUpdateAsync(setters => setters.SetProperty(fact => fact.Amount, 20), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Uses the independent parent-list oracle rather than repeating the query's bounds predicates.</summary>
    private static CompositionSnapshotRow[] ExpectedSnapshot(
        bool afterChange
    )
    {
        (int Upper, int Distance)[] ancestors = afterChange ? [(1, 2), (3, 0), (5, 1)] : [(1, 2), (2, 1), (3, 0)];

        return ancestors
            .Select(ancestor => new CompositionSnapshotRow
            {
                Upper = ancestor.Upper,
                Parent = afterChange ? 5 : 2,
                DownDistance = 2,
                UpDistance = ancestor.Distance,
                Matches = afterChange,
                FromDate = new DateOnly(2044, afterChange ? 9 : 10, 1),
                ToDate = afterChange ? new DateOnly(2044, 10, 31) : null,
                Amount = afterChange ? 20 : 10,
            })
            .ToArray();
    }
}
