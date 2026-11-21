namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class AncestorIndexSafeMigrationTests
{
    /// <summary>Applies and replays a populated generated-column upgrade without changing hierarchy rows.</summary>
    /// <param name="engine">The real relational engine and its optional adapter.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task AnalyzePendingMigrationsAsync_PopulatedAncestorUpgrade_PreservesRowsWithOptionalAdapter(
        string engine
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var initial = database.Scaffold(false, "AOrdinarySafeInitial");
        await database.MigrateAsync(
            initial,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: false,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        await database.PhysicalDatabase.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var before = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var indexesBefore = IndexSignatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var upgrade = database.Scaffold(true, "BAddSafeAncestorIndex", initial);
        await using var context = database.CreateContext(assembly: upgrade.Assembly);
        var operations = upgrade
            .GetMigration(context, 1)
            .UpOperations
            .ToArray();

        var generatedColumnsBefore = await ReadGeneratedColumnCountAsync(
            context,
            engine,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var preflight = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzePendingMigrationsAsync(
                context,
                new SafeMigrationRunOptions("nested-set-populated-ancestor-upgrade"),
                CancellationToken.None);

        var afterPreflight = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var indexesAfterPreflight = IndexSignatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var historyAfterPreflight =
            (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        var generatedColumnsAfterPreflight = await ReadGeneratedColumnCountAsync(
            context,
            engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await database.MigrateAsync(
            upgrade,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        var after = await database.PhysicalDatabase.ReadNodesAsync(
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
        await SafeMigrationTestServices.ExecuteOperationsAsync(
            context,
            operations,
            cancellationToken: TestContext.Current.CancellationToken);

        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-populated-ancestor-replay"),
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
        Assert.Equal(SafeMigrationReportStatus.Ready, preflight.Status);
        Assert.Contains(
            preflight.Assessments,
            assessment => assessment is
            {
                OperationKind: SafeMigrationOperationKind.EnsureColumn, Action: SafeMigrationAction.Apply,
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
}
