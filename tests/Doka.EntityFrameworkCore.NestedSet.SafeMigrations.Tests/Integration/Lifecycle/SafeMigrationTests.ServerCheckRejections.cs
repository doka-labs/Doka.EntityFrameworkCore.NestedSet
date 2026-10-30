namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>Rejects each invalid legacy coordinate while preserving the complete database and history.</summary>
    /// <param name="engine">The native server engine and optional adapter.</param>
    /// <param name="checkName">The single violated generated predicate.</param>
    /// <returns>A task that completes after read-only preflight and guarded runtime rejection.</returns>
    [Theory]
    [InlineData("MySql", "LeftMin")]
    [InlineData("MySql", "RightAfterLeft")]
    [InlineData("MySql", "DepthMin")]
    [InlineData("MySql", "PositionMin")]
    [InlineData("MariaDb", "LeftMin")]
    [InlineData("MariaDb", "RightAfterLeft")]
    [InlineData("MariaDb", "DepthMin")]
    [InlineData("MariaDb", "PositionMin")]
    [InlineData("PostgreSql", "LeftMin")]
    [InlineData("PostgreSql", "RightAfterLeft")]
    [InlineData("PostgreSql", "DepthMin")]
    [InlineData("PostgreSql", "PositionMin")]
    public async Task InvalidLegacyCoordinatesRejectSupportedChecksWithoutMutation(
        string engine,
        string checkName
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine, engine == "PostgreSql");
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);

        // WHY: The historical model has no checks. Direct insertion represents corrupt pre-adoption data
        // without weakening either managed hierarchy writes or the new migration's predicates.
        await database.InsertNodeDirectlyAsync(CreateInvalidCoordinateNode(checkName));
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var operations = GetUpOperations(upgrade, context, 1)
            .OfType<SafeMigrationOperation>()
            .Where(operation => operation.Intent is EnsureCheckConstraintIntent)
            .ToArray();

        var rejected = operations.Single(operation =>
            ((EnsureCheckConstraintIntent)operation.Intent).Definition.Name.Contains(
                checkName,
                StringComparison.Ordinal));

        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var checksBefore = await database.ReadChecksAsync();
        var rowsBefore = await database.ReadNodesAsync(MigrationStage.Baseline);
        var columnsBefore = await database.ReadColumnTypesAsync();
        var foreignKeysBefore = ForeignKeySignatures(await database.ReadForeignKeysAsync());
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-invalid-legacy-check"),
                CancellationToken.None);

        // WHY: Executing only the rejected check tests its guard before mutation; a complete MySQL upgrade
        // may implicitly commit earlier DDL and cannot promise transactional rollback of the whole chain.
        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(context, [rejected]));

        var indexesAfter = IndexSignatures(await database.ReadIndexesAsync());
        var checksAfter = await database.ReadChecksAsync();
        var rowsAfter = await database.ReadNodesAsync(MigrationStage.Baseline);
        var columnsAfter = await database.ReadColumnTypesAsync();
        var foreignKeysAfter = ForeignKeySignatures(await database.ReadForeignKeysAsync());
        var historyAfter = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        Assert.Equal(4, report.Assessments.Count);
        var blocker = Assert.Single(
            report.Assessments,
            assessment => assessment.Action == SafeMigrationAction.RejectDataBlocked);

        Assert.Equal(((EnsureCheckConstraintIntent)rejected.Intent).Definition.Name, blocker.ObjectName);
        Assert.Equal(SafeMigrationObservedState.DataBlocked, blocker.ObservedState);
        Assert.Equal("classified_data_blocked", blocker.AnalysisCode);
        Assert.Equal("data_blocked", blocker.DecisionCode);
        Assert.False(blocker.PostconditionSatisfied);
        Assert.Equal(3, report.Assessments.Count(assessment => assessment.Action == SafeMigrationAction.Apply));
        Assert.NotNull(exception);
        Assert.Contains("doka_sm_data_blocked", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(checksBefore, checksAfter);
        Assert.Equal(rowsBefore, rowsAfter);
        Assert.Equal(columnsBefore, columnsAfter);
        Assert.Equal(foreignKeysBefore, foreignKeysAfter);
        Assert.Equal(baseline.MigrationIds, historyBefore);
        Assert.Equal(historyBefore, historyAfter);
    }
}
