namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class AncestorIndexSafeMigrationTests
{
    /// <summary>Creates and replays the exact generated schema through the optional adapters.</summary>
    /// <param name="engine">The actual relational provider and optional adapter.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task AnalyzePendingMigrationsAsync_FreshComputedTableMigration_PreservesFacetsHistoryAndReplay(
        string engine
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var chain = database.Scaffold(true, "AncestorSafeInitial");
        await using var context = database.CreateContext(assembly: chain.Assembly);

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzePendingMigrationsAsync(
                context,
                new SafeMigrationRunOptions("nested-set-fresh-ancestor-column"),
                CancellationToken.None);

        var tablesAfterPreflight = await ReadHierarchyTableCountAsync(
            context,
            engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var historyAfterPreflight =
            (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        await database.MigrateAsync(
            chain,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        await database.PhysicalDatabase.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var beforeReplay = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var flags = await ReadFlagsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var schema = await ReadGeneratedColumnAsync(
            context,
            engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var indexes = await database.PhysicalDatabase.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var history = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();
        var operations = chain
            .GetMigration(context, 0)
            .UpOperations
            .ToArray();

        await SafeMigrationTestServices.ExecuteOperationsAsync(
            context,
            operations,
            cancellationToken: TestContext.Current.CancellationToken);

        var replay = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-fresh-ancestor-replay"),
                CancellationToken.None);

        var afterReplay = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var flagsAfterReplay = await ReadFlagsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var schemaAfterReplay = await ReadGeneratedColumnAsync(
            context,
            engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var indexesAfterReplay = IndexSignatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var historyAfterReplay = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Assert
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        Assert.Contains(
            report.Assessments,
            assessment => assessment is
            {
                OperationKind: SafeMigrationOperationKind.EnsureTable, Action: SafeMigrationAction.Apply,
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

    /// <summary>
    /// Rejects a genuinely different ordinary SQLite generated column without changing rows or history.
    /// </summary>
    [Fact]
    [DatabasePlatform("Sqlite")]
    public async Task AnalyzeAsync_OrdinarySqliteComputedFacets_RejectGuardedTableReplayWithoutMutation()
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(
            "Sqlite",
            cancellationToken: TestContext.Current.CancellationToken);

        var chain = database.Scaffold(true, "AncestorOrdinaryInitial", enableIntegration: false);
        await database.MigrateAsync(
            chain,
            enableIntegration: false,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            splitStructure: false);

        await database.PhysicalDatabase.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var guarded = database.Scaffold(true, "AncestorSafeInitial");
        await using var context = database.CreateContext(assembly: guarded.Assembly);
        var operation = Assert.Single(
            guarded
                .GetMigration(context, 0)
                .UpOperations
                .OfType<SafeMigrationOperation>(),
            value => value.Intent is EnsureTableIntent { Definition.Table: MigrationContext.TableName });

        var before = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var schemaBefore = await ReadGeneratedColumnAsync(
            context,
            "Sqlite",
            cancellationToken: TestContext.Current.CancellationToken);

        var indexesBefore = IndexSignatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

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
            SafeMigrationTestServices.ExecuteOperationsAsync(
                context,
                [operation],
                cancellationToken: TestContext.Current.CancellationToken));

        var after = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var schemaAfter = await ReadGeneratedColumnAsync(
            context,
            "Sqlite",
            cancellationToken: TestContext.Current.CancellationToken);

        var indexesAfter = IndexSignatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

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
}
