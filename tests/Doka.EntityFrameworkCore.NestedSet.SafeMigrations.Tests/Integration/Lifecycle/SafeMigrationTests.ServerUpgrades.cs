namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>Applies the populated integer upgrade and replays its safe operations against the resulting catalog.</summary>
    /// <param name="engine">The native server engine and its optional adapter.</param>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit schema or database qualifier.</param>
    /// <returns>A task that completes after checking schema, data, capacity, history, and actual guard replay.</returns>
    [Theory]
    [InlineData("PostgreSql", true)]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task ServerCoordinateUpgradePreservesDataHistoryAndReplays(
        string engine,
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine, qualifySchema);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        var before = await database.ReadNodesAsync(MigrationStage.Baseline);
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);

        // WHY: Replay the generated safe intents, including guarded index drops, so migration history
        // cannot hide a broken runtime postcondition or no-op guard.
        var replayOperations = GetUpOperations(upgrade, context, 1)
            .OfType<SafeMigrationOperation>()
            .ToArray();

        var beyondInt32 = (long)int.MaxValue + 1;

        // Act
        await database.MigrateAsync(upgrade);
        var after = await database.ReadNodesAsync();
        await database.InsertNodeDirectlyAsync(
            new MigrationNode
            {
                NodeId = 43,
                Scope = 7,
                TreeId = 19,
                Left = beyondInt32,
                Right = beyondInt32 + 1,
                Position = beyondInt32,
                Payload = "upgraded Int64 coordinates",
            });

        var nodesBeforeReplay = await database.ReadNodesAsync();
        var indexes = await database.ReadIndexesAsync();
        var checks = await database.ReadChecksAsync();
        var foreignKeys = await database.ReadForeignKeysAsync();
        var columnTypes = await database.ReadColumnTypesAsync();
        var history = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();
        await SafeMigrationTestServices.ExecuteOperationsAsync(context, replayOperations);
        var replayReport = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                replayOperations,
                new SafeMigrationRunOptions("nested-set-supported-upgrade-replay"),
                CancellationToken.None);

        var nodesAfterReplay = await database.ReadNodesAsync();
        var indexesAfterReplay = await database.ReadIndexesAsync();
        var checksAfterReplay = await database.ReadChecksAsync();
        var foreignKeysAfterReplay = await database.ReadForeignKeysAsync();
        var columnsAfterReplay = await database.ReadColumnTypesAsync();
        var historyAfterReplay = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, before.Length);
        Assert.Equal(before, after);
        AssertCurrentPhysicalContract(engine, indexes, checks, foreignKeys, columnTypes);
        Assert.Equal(upgrade.MigrationIds, history);
        var largeNode = Assert.Single(nodesBeforeReplay, node => node.NodeId == 43);

        Assert.Equal(
            (beyondInt32, beyondInt32 + 1, beyondInt32),
            (largeNode.Left, largeNode.Right, largeNode.Position));
        Assert.NotEmpty(replayOperations);
        Assert.Equal(SafeMigrationReportStatus.Ready, replayReport.Status);
        Assert.Equal(replayOperations.Length, replayReport.Assessments.Count);
        Assert.Equal(
            3,
            replayReport.Assessments.Count(assessment =>
                assessment.OperationKind == SafeMigrationOperationKind.DropIndex));
        Assert.All(
            replayReport.Assessments,
            assessment =>
            {
                // WHY: An idempotent drop succeeds when its old object is absent; retained objects must match.
                var expectedState = assessment.OperationKind == SafeMigrationOperationKind.DropIndex
                    ? SafeMigrationObservedState.Missing
                    : SafeMigrationObservedState.Matching;

                Assert.Equal(expectedState, assessment.ObservedState);
                Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
                Assert.True(assessment.PostconditionSatisfied);
            });

        Assert.Equal(nodesBeforeReplay, nodesAfterReplay);
        Assert.Equal(IndexSignatures(indexes), IndexSignatures(indexesAfterReplay));
        Assert.Equal(checks, checksAfterReplay);
        Assert.Equal(ForeignKeySignatures(foreignKeys), ForeignKeySignatures(foreignKeysAfterReplay));
        Assert.Equal(columnTypes, columnsAfterReplay);
        Assert.Equal(history, historyAfterReplay);
    }
}
