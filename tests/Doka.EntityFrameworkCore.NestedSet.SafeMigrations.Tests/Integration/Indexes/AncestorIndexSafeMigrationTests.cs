namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

/// <summary>Qualifies the optional ancestor index when applications also enable SafeMigrations.</summary>
[DatabasePlatform]
public sealed partial class AncestorIndexSafeMigrationTests : IClassFixture<MigrationFixture>
{
    private readonly MigrationFixture _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Creates independent optional-adapter scenarios on reusable engines.</summary>
    /// <param name="fixture">The ordinary migration fixture.</param>
    /// <param name="output">The retained diagnostic output for known adapter boundaries.</param>
    public AncestorIndexSafeMigrationTests(
        MigrationFixture fixture,
        ITestOutputHelper output
    )
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>Accepts and safely replays the exact generated ancestor-index definition.</summary>
    /// <param name="engine">The real relational engine and its optional adapter.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task GeneratedAncestorIndexReplaysWithoutChangingData(
        string engine
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine);
        var chain = database.Scaffold(true, "AncestorSafeInitial");

        // WHY: Index replay also has to work on an ordinary EF-created schema; it must not depend on
        // the optional adapter having created the table or adjusted SQLite's generated-column facets.
        var ordinary = database.Scaffold(true, "AncestorOrdinaryInitial", enableIntegration: false);
        await database.MigrateAsync(ordinary, enableIntegration: false);
        await database.PhysicalDatabase.SeedAsync();
        await using var context = database.CreateContext(assembly: chain.Assembly);
        var operation = AncestorOperation(chain, context);
        var before = await database.PhysicalDatabase.ReadNodesAsync();
        var indexesBefore = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        await SafeMigrationTestServices.ExecuteOperationsAsync(context, [operation]);
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                [operation],
                new SafeMigrationRunOptions("nested-set-ancestor-index-replay"),
                CancellationToken.None);

        var after = await database.PhysicalDatabase.ReadNodesAsync();
        var indexesAfter = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var historyAfter = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Assert
        Assert.Equal(before, after);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(ordinary.MigrationIds, historyBefore);
        Assert.Equal(historyBefore, historyAfter);
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        var assessment = Assert.Single(report.Assessments);
        Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
        Assert.Equal(
            engine == "PostgreSql" ? "tree_id IS NOT NULL" : null,
            ((EnsureIndexIntent)operation.Intent).Definition.Filter);
    }

    /// <summary>Rejects an externally replaced ancestor index while retaining all application data.</summary>
    /// <param name="engine">The real relational engine and its optional adapter.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task AncestorIndexDriftFailsClosedAndPreservesData(
        string engine
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine);
        var chain = database.Scaffold(true, "AncestorSafeInitial");
        var ordinary = database.Scaffold(true, "AncestorOrdinaryInitial", enableIntegration: false);
        await database.MigrateAsync(ordinary, enableIntegration: false);
        await database.PhysicalDatabase.SeedAsync();
        await using var context = database.CreateContext(assembly: chain.Assembly);
        var operation = AncestorOperation(chain, context);
        var definition = ((EnsureIndexIntent)operation.Intent).Definition;
        await SafeMigrationTestServices.IntroduceIndexDriftAsync(context, definition, "entry_depth");
        var before = await database.PhysicalDatabase.ReadNodesAsync();
        var indexesBefore = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                [operation],
                new SafeMigrationRunOptions("nested-set-ancestor-index-drift"),
                CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(context, [operation]));

        var after = await database.PhysicalDatabase.ReadNodesAsync();
        var indexesAfter = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var historyAfter = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Assert
        Assert.NotNull(exception);
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        var assessment = Assert.Single(report.Assessments);
        Assert.Equal(SafeMigrationObservedState.Different, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.RejectDifferent, assessment.Action);
        Assert.Equal(before, after);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(ordinary.MigrationIds, historyBefore);
        Assert.Equal(historyBefore, historyAfter);
    }

    /// <summary>Applies and replays a populated generated-column upgrade without changing hierarchy rows.</summary>
    /// <param name="engine">The real relational engine and its optional adapter.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task PopulatedAncestorUpgradePreservesRowsWithOptionalAdapter(
        string engine
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine);
        var initial = database.Scaffold(false, "AOrdinarySafeInitial");
        await database.MigrateAsync(initial, false);
        await database.PhysicalDatabase.SeedAsync();
        var before = await database.PhysicalDatabase.ReadNodesAsync();
        var indexesBefore = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var upgrade = database.Scaffold(true, "BAddSafeAncestorIndex", initial);
        await using var context = database.CreateContext(assembly: upgrade.Assembly);
        var operations = upgrade
            .GetMigration(context, 1)
            .UpOperations
            .ToArray();

        var generatedColumnsBefore = await ReadGeneratedColumnCountAsync(context, engine);

        // Act
        var preflight = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzePendingMigrationsAsync(
                context,
                new SafeMigrationRunOptions("nested-set-populated-ancestor-upgrade"),
                CancellationToken.None);

        var afterPreflight = await database.PhysicalDatabase.ReadNodesAsync();
        var indexesAfterPreflight = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var historyAfterPreflight =
            (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        var generatedColumnsAfterPreflight = await ReadGeneratedColumnCountAsync(context, engine);
        await database.MigrateAsync(upgrade);
        var after = await database.PhysicalDatabase.ReadNodesAsync();
        var flags = await ReadFlagsAsync(context);
        var schema = await ReadGeneratedColumnAsync(context, engine);
        var indexes = await database.PhysicalDatabase.ReadIndexesAsync();
        var history = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();
        await SafeMigrationTestServices.ExecuteOperationsAsync(context, operations);
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-populated-ancestor-replay"),
                CancellationToken.None);

        var afterReplay = await database.PhysicalDatabase.ReadNodesAsync();
        var flagsAfterReplay = await ReadFlagsAsync(context);
        var schemaAfterReplay = await ReadGeneratedColumnAsync(context, engine);
        var indexesAfterReplay = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var historyAfterReplay = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Assert
        Assert.Equal(SafeMigrationReportStatus.Ready, preflight.Status);
        Assert.Contains(
            preflight.Assessments,
            assessment => assessment is
            {
                OperationKind: SafeMigrationOperationKind.EnsureColumn, Action: SafeMigrationAction.Apply
            });
        Assert.Equal(before, afterPreflight);
        Assert.Equal(indexesBefore, indexesAfterPreflight);
        Assert.Equal(initial.MigrationIds, historyAfterPreflight);
        Assert.Equal(0, generatedColumnsBefore);
        Assert.Equal(0, generatedColumnsAfterPreflight);
        Assert.Equal(before, after);
        Assert.Equal([true, false], flags);
        AssertGeneratedColumn(schema, engine);
        AssertAncestorIndex(indexes, engine);
        Assert.Equal(upgrade.MigrationIds, history);
        AssertMatchingReplay(report, operations.Length);
        Assert.Equal(before, afterReplay);
        Assert.Equal(flags, flagsAfterReplay);
        Assert.Equal(schema, schemaAfterReplay);
        Assert.Equal(IndexSignatures(indexes), indexesAfterReplay);
        Assert.Equal(upgrade.MigrationIds, historyAfterReplay);
    }

    /// <summary>Creates and replays the exact generated schema through the optional adapters.</summary>
    /// <param name="engine">The actual relational provider and optional adapter.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task FreshComputedTableMigrationPreservesFacetsHistoryAndReplay(
        string engine
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine);
        var chain = database.Scaffold(true, "AncestorSafeInitial");
        await using var context = database.CreateContext(assembly: chain.Assembly);

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzePendingMigrationsAsync(
                context,
                new SafeMigrationRunOptions("nested-set-fresh-ancestor-column"),
                CancellationToken.None);

        var tablesAfterPreflight = await ReadHierarchyTableCountAsync(context, engine);
        var historyAfterPreflight =
            (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        await database.MigrateAsync(chain);
        await database.PhysicalDatabase.SeedAsync();
        var beforeReplay = await database.PhysicalDatabase.ReadNodesAsync();
        var flags = await ReadFlagsAsync(context);
        var schema = await ReadGeneratedColumnAsync(context, engine);
        var indexes = await database.PhysicalDatabase.ReadIndexesAsync();
        var history = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();
        var operations = chain
            .GetMigration(context, 0)
            .UpOperations
            .ToArray();

        await SafeMigrationTestServices.ExecuteOperationsAsync(context, operations);
        var replay = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-fresh-ancestor-replay"),
                CancellationToken.None);

        var afterReplay = await database.PhysicalDatabase.ReadNodesAsync();
        var flagsAfterReplay = await ReadFlagsAsync(context);
        var schemaAfterReplay = await ReadGeneratedColumnAsync(context, engine);
        var indexesAfterReplay = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var historyAfterReplay = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Assert
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        Assert.Contains(
            report.Assessments,
            assessment => assessment is
            {
                OperationKind: SafeMigrationOperationKind.EnsureTable, Action: SafeMigrationAction.Apply
            });
        Assert.Equal(0, tablesAfterPreflight);
        Assert.Empty(historyAfterPreflight);
        Assert.Equal(2, beforeReplay.Length);
        Assert.Equal([true, false], flags);
        AssertGeneratedColumn(schema, engine);
        AssertAncestorIndex(indexes, engine);
        Assert.Equal(chain.MigrationIds, history);
        AssertMatchingReplay(replay, operations.Length);
        Assert.Equal(beforeReplay, afterReplay);
        Assert.Equal(flags, flagsAfterReplay);
        Assert.Equal(schema, schemaAfterReplay);
        Assert.Equal(IndexSignatures(indexes), indexesAfterReplay);
        Assert.Equal(chain.MigrationIds, historyAfterReplay);
    }

    /// <summary>Rejects a genuinely different ordinary SQLite generated column without changing rows or history.</summary>
    [Fact]
    [DatabasePlatform("Sqlite")]
    public async Task OrdinarySqliteComputedFacetsRejectGuardedTableReplayWithoutMutation()
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("Sqlite");
        var chain = database.Scaffold(true, "AncestorOrdinaryInitial", enableIntegration: false);
        await database.MigrateAsync(chain, enableIntegration: false);
        await database.PhysicalDatabase.SeedAsync();
        var guarded = database.Scaffold(true, "AncestorSafeInitial");
        await using var context = database.CreateContext(assembly: guarded.Assembly);
        var operation = Assert.Single(
            guarded
                .GetMigration(context, 0)
                .UpOperations
                .OfType<SafeMigrationOperation>(),
            value => value.Intent is EnsureTableIntent { Definition.Table: MigrationContext.TableName });

        var before = await database.PhysicalDatabase.ReadNodesAsync();
        var schemaBefore = await ReadGeneratedColumnAsync(context, "Sqlite");
        var indexesBefore = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                [operation],
                new SafeMigrationRunOptions("nested-set-ordinary-sqlite-generated-drift"),
                CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(context, [operation]));

        var after = await database.PhysicalDatabase.ReadNodesAsync();
        var schemaAfter = await ReadGeneratedColumnAsync(context, "Sqlite");
        var indexesAfter = IndexSignatures(await database.PhysicalDatabase.ReadIndexesAsync());
        var historyAfter = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        var assessment = Assert.Single(report.Assessments);
        Assert.Equal(SafeMigrationObservedState.Different, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.RejectDifferent, assessment.Action);
        Assert.Empty(schemaBefore.StoreType);
        Assert.True(schemaBefore.IsNullable);
        Assert.False(schemaBefore.IsStored);
        Assert.Equal(before, after);
        Assert.Equal(schemaBefore, schemaAfter);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(chain.MigrationIds, historyBefore);
        Assert.Equal(historyBefore, historyAfter);
    }

    /// <summary>Configures both provider-specific adapter registration paths on an isolated database.</summary>
    private Task<AncestorIndexDatabase> CreateDatabaseAsync(
        string engine
    ) => AncestorIndexDatabase.CreateAsync(
        _fixture,
        engine,
        options => SafeMigrationTestServices.ConfigureOptions(options, engine),
        SafeMigrationTestServices.ConfigureDesignServices);

    /// <summary>Finds the actual scaffolded safe intent by the complete ordered key definition.</summary>
    private static SafeMigrationOperation AncestorOperation(
        MigrationChain chain,
        DbContext context,
        int migrationIndex = 0
    ) => Assert.Single(
        chain
            .GetMigration(context, migrationIndex)
            .UpOperations
            .OfType<SafeMigrationOperation>(),
        operation => operation.Intent is EnsureIndexIntent intent
            && intent
                .Definition
                .Keys
                .Select(key => key.Column)
                .SequenceEqual(
                [
                    "tree_scope",
                    AncestorIndexContext.FlagColumn,
                    "tree_id",
                    "right_bound",
                    "left_bound",
                    "entry_id",
                ]));

    /// <summary>Captures complete physical index keys for adversarial before/after comparisons.</summary>
    private static string[] IndexSignatures(
        MigrationIndex[] indexes
    ) => indexes
        .Select(x =>
            x.Name
            + ":"
            + string.Join(",", x.Columns)
            + ":"
            + string.Join(",", x.Descending)
            + ":"
            + x.IsUnique
            + ":"
            + x.IsOrdinary)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
