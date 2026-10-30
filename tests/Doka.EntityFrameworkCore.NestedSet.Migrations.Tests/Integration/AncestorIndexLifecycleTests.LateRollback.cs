using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexLifecycleTests
{
    /// <summary>Refreshes generated values after a proven structural parent move.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task ParentSaveRefreshesFlagsAfterObservedStructuralWrite(
        string engine
    )
    {
        // Arrange
        var probe = new StructuralRefreshProbe(failRefresh: false);
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            options => options.AddInterceptors(probe));

        await using var context = await CreatePendingParentMoveAsync(database);
        probe.Armed = true;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        probe.Armed = false;
        var persisted = await ReadLifecycleStatesAsync(context);
        var tracked = ReadTrackedLifecycleStates(context);
        var original = ReadOriginalLifecycleStates(context);

        // Assert
        AssertStructuralRefreshReached(probe);
        Assert.Equal(probe.DuringMove, persisted);
        Assert.Equal(persisted, tracked);
        Assert.Equal(persisted, original);
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.Flag));
        Assert.All(context.ChangeTracker.Entries<MigrationNode>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    /// <summary>Restores complete tracked and persisted state after a proven late structural failure.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task LateStructuralFailureRestoresGeneratedFlagsAndPendingParentEdit(
        string engine
    )
    {
        // Arrange
        var probe = new StructuralRefreshProbe(failRefresh: true);
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            options => options.AddInterceptors(probe));

        await using var context = await CreatePendingParentMoveAsync(database);
        var persistedBefore = await ReadLifecycleStatesAsync(context);
        var pendingBefore = ReadTrackedLifecycleStates(context);
        var originalBefore = ReadOriginalLifecycleStates(context);
        probe.Armed = true;

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        probe.Armed = false;
        var persisted = await ReadLifecycleStatesAsync(context);
        var tracked = ReadTrackedLifecycleStates(context);
        var original = ReadOriginalLifecycleStates(context);

        // Assert
        AssertStructuralRefreshReached(probe);
        var failure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(StructuralRefreshProbe.FailureMessage, failure.Message);
        Assert.Equal(persistedBefore, persisted);
        Assert.Equal(pendingBefore, tracked);
        Assert.Equal(originalBefore, original);
        Assert.Equal(
            EntityState.Modified,
            context.ChangeTracker.Entries<MigrationNode>().Single(entry => entry.Entity.NodeId == 2).State);
    }

    /// <summary>Persists a restored pending edit after an arranged structural failure.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task RestoredParentEditCanBeSavedAfterLateStructuralFailure(
        string engine
    )
    {
        // Arrange
        var probe = new StructuralRefreshProbe(failRefresh: true);
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            options => options.AddInterceptors(probe));

        await using var context = await CreatePendingParentMoveAsync(database);
        var persistedBefore = await ReadLifecycleStatesAsync(context);
        var pendingBefore = ReadTrackedLifecycleStates(context);
        var originalBefore = ReadOriginalLifecycleStates(context);
        probe.Armed = true;
        var priorFailure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        probe.Armed = false;
        var restoredDatabase = await ReadLifecycleStatesAsync(context);
        var restoredTracked = ReadTrackedLifecycleStates(context);
        var restoredOriginal = ReadOriginalLifecycleStates(context);

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var persisted = await ReadLifecycleStatesAsync(context);
        var tracked = ReadTrackedLifecycleStates(context);
        var original = ReadOriginalLifecycleStates(context);

        // Assert
        AssertStructuralRefreshReached(probe);
        var failure = Assert.IsType<InvalidOperationException>(priorFailure);
        Assert.Equal(StructuralRefreshProbe.FailureMessage, failure.Message);
        Assert.Equal(persistedBefore, restoredDatabase);
        Assert.Equal(pendingBefore, restoredTracked);
        Assert.Equal(originalBefore, restoredOriginal);
        Assert.Equal(probe.DuringMove, persisted);
        Assert.Equal(persisted, tracked);
        Assert.Equal(persisted, original);
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.Flag));
        Assert.All(context.ChangeTracker.Entries<MigrationNode>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    /// <summary>Creates the shared migrated topology and one detected pending cross-tree parent edit.</summary>
    private static async Task<AncestorIndexContext> CreatePendingParentMoveAsync(
        AncestorIndexDatabase database
    )
    {
        var chain = database.Scaffold(true, "AncestorLateStructuralRollback");
        await database.MigrateAsync(chain);
        var context = database.CreateContext();

        try
        {
            await SeedAsync(context);
            await context
                .Set<MigrationNode>()
                .LoadAsync(CancellationToken.None);

            var moved = context
                .ChangeTracker
                .Entries<MigrationNode>()
                .Single(entry => entry.Entity.NodeId == 2)
                .Entity;

            moved.ParentId = 4;
            context.ChangeTracker.DetectChanges();

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Proves a completed parent move was observed before the generated-value refresh.</summary>
    private static void AssertStructuralRefreshReached(
        StructuralRefreshProbe probe
    )
    {
        Assert.True(probe.StructuralWriteObserved);
        Assert.True(probe.GeneratedRefreshObserved);
        Assert.Equal([2, 4], probe.DuringMove.Where(row => row.Flag).Select(row => row.Id));
        Assert.Equal(4, Assert.Single(probe.DuringMove, row => row.Id == 2).Parent);
        Assert.Equal(12, Assert.Single(probe.DuringMove, row => row.Id == 2).Tree);
    }

    /// <summary>Reads the complete structural and generated state without tracker identity resolution.</summary>
    private static Task<LifecycleState[]> ReadLifecycleStatesAsync(
        AncestorIndexContext context
    ) => context
        .Set<MigrationNode>()
        .AsNoTracking()
        .OrderBy(x => x.NodeId)
        .Select(x => new LifecycleState(
            x.NodeId,
            x.Scope,
            x.TreeId,
            x.ParentId,
            x.Left,
            x.Right,
            x.Depth,
            x.Position,
            EF.Property<bool>(x, AncestorIndexContext.FlagProperty)))
        .ToArrayAsync(CancellationToken.None);

    /// <summary>Captures CLR structure and the current generated shadow sidecar.</summary>
    private static LifecycleState[] ReadTrackedLifecycleStates(
        AncestorIndexContext context
    ) => context
        .ChangeTracker
        .Entries<MigrationNode>()
        .OrderBy(x => x.Entity.NodeId)
        .Select(x => new LifecycleState(
            x.Entity.NodeId,
            x.Entity.Scope,
            x.Entity.TreeId,
            x.Entity.ParentId,
            x.Entity.Left,
            x.Entity.Right,
            x.Entity.Depth,
            x.Entity.Position,
            x.Property<bool>(AncestorIndexContext.FlagProperty).CurrentValue))
        .ToArray();

    /// <summary>Captures original structure and generated sidecars independently of pending CLR edits.</summary>
    private static LifecycleState[] ReadOriginalLifecycleStates(
        AncestorIndexContext context
    ) => context
        .ChangeTracker
        .Entries<MigrationNode>()
        .OrderBy(x => x.Entity.NodeId)
        .Select(x => new LifecycleState(
            x.Property(row => row.NodeId).OriginalValue,
            x.Property(row => row.Scope).OriginalValue,
            x.Property(row => row.TreeId).OriginalValue,
            x.Property(row => row.ParentId).OriginalValue,
            x.Property(row => row.Left).OriginalValue,
            x.Property(row => row.Right).OriginalValue,
            x.Property(row => row.Depth).OriginalValue,
            x.Property(row => row.Position).OriginalValue,
            x.Property<bool>(AncestorIndexContext.FlagProperty).OriginalValue))
        .ToArray();

    /// <summary>A comparable snapshot of one complete structural row and its derived shadow marker.</summary>
    /// <param name="Id">The node identity.</param>
    /// <param name="Scope">The isolation scope.</param>
    /// <param name="Tree">The stable tree identity.</param>
    /// <param name="Parent">The direct parent identity.</param>
    /// <param name="Left">The left boundary.</param>
    /// <param name="Right">The right boundary.</param>
    /// <param name="Depth">The structural depth.</param>
    /// <param name="Position">The sibling position.</param>
    /// <param name="Flag">The generated branch marker.</param>
    private sealed record LifecycleState(
        int Id,
        int Scope,
        int Tree,
        int? Parent,
        long Left,
        long Right,
        int Depth,
        long Position,
        bool Flag
    );

    /// <summary>Observes structural writes and fails only at their later generated-value refresh boundary.</summary>
    private sealed class StructuralRefreshProbe(bool failRefresh) : DbCommandInterceptor
    {
        /// <summary>The unique failure text asserted after the transaction has been rolled back.</summary>
        internal const string FailureMessage = "Deliberate failure after ancestor-index structural write.";

        /// <summary>Gets or sets whether the operation under test is being observed.</summary>
        internal bool Armed { get; set; }

        /// <summary>Gets whether a real hierarchy-coordinate UPDATE completed.</summary>
        internal bool StructuralWriteObserved { get; private set; }

        /// <summary>Gets whether the subsequent generated-value refresh was reached.</summary>
        internal bool GeneratedRefreshObserved { get; private set; }

        /// <summary>Gets the database state read within the writer transaction before a possible failure.</summary>
        internal LifecycleState[] DuringMove { get; private set; } = [];

        /// <inheritdoc />
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(MigrationContext.TableName, StringComparison.Ordinal)
                && command.CommandText.Contains("right_bound", StringComparison.Ordinal))
            {
                StructuralWriteObserved = true;
            }

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Armed
                || !StructuralWriteObserved
                || GeneratedRefreshObserved
                || !command.CommandText.Contains(AncestorIndexContext.FlagColumn, StringComparison.Ordinal))
            {
                return result;
            }

            GeneratedRefreshObserved = true;
            var context = (AncestorIndexContext)eventData.Context!;
            var sql = context.GetService<ISqlGenerationHelper>();
            string[] columns =
            [
                "entry_id",
                "tree_scope",
                "tree_id",
                "parent_entry",
                "left_bound",
                "right_bound",
                "entry_depth",
                "sibling_position",
                AncestorIndexContext.FlagColumn,
            ];

            // WHY: A raw command shares the exact writer connection/transaction but bypasses EF interception.
            // It proves the generated values really changed before the deliberate failure is raised.
            await using var snapshot = command.Connection!.CreateCommand();
            snapshot.Transaction = command.Transaction;
            snapshot.CommandText = "SELECT "
                + string.Join(", ", columns.Select(sql.DelimitIdentifier))
                + " FROM "
                + sql.DelimitIdentifier(MigrationContext.TableName, context.Schema)
                + " ORDER BY "
                + sql.DelimitIdentifier("entry_id");

            var rows = new List<LifecycleState>();
            await using var reader = await snapshot.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var parentIsNull = await reader.IsDBNullAsync(3, cancellationToken);
                rows.Add(
                    new LifecycleState(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        parentIsNull ? null : reader.GetInt32(3),
                        reader.GetInt64(4),
                        reader.GetInt64(5),
                        reader.GetInt32(6),
                        reader.GetInt64(7),
                        reader.GetBoolean(8)));
            }

            DuringMove = rows.ToArray();

            return failRefresh
                ? throw new InvalidOperationException(FailureMessage)
                : result;
        }
    }
}
