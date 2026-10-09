namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>Verifies replay reaches runtime guards and preserves the physical index catalog and data.</summary>
    /// <param name="engine">The relational database engine.</param>
    /// <param name="qualifySchema">Whether the model preserves an explicit schema or database qualifier.</param>
    /// <returns>A task that completes after directly replaying the generated index operations.</returns>
    [Theory]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task GeneratedIndexOperationsReplayWithoutCatalogOrDataChanges(
        string engine,
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine, qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain);
        await database.SeedAsync();
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = GetIndexOperations(chain, context);
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var nodesBefore = await database.ReadNodesAsync();

        // Act
        await SafeMigrationTestServices.ExecuteOperationsAsync(context, operations);
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-generated-index-replay"),
                CancellationToken.None);

        var indexesAfter = IndexSignatures(await database.ReadIndexesAsync());
        var nodesAfter = await database.ReadNodesAsync();

        // Assert
        AssertFreshIndexOperations(engine, operations);
        Assert.All(
            operations,
            operation => Assert.Equal(database.Schema, ((EnsureIndexIntent)operation.Intent).Definition.Schema));
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        Assert.Equal(operations.Length, report.Assessments.Count);
        Assert.All(
            report.Assessments,
            assessment =>
            {
                Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
                Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
            });
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(nodesBefore, nodesAfter);
    }

    /// <summary>Verifies a same-name index with wrong columns is rejected without repairing external drift.</summary>
    /// <param name="engine">The relational database engine.</param>
    /// <param name="qualifySchema">Whether the model preserves an explicit schema or database qualifier.</param>
    /// <returns>A task that completes after probing the generated operation against a corrupted index.</returns>
    [Theory]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task SameNameWrongColumnIndexFailsClosedAndPreservesData(
        string engine,
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine, qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain);
        await database.SeedAsync();
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operation = GetIndexOperations(chain, context)
            .Single(candidate => ((EnsureIndexIntent)candidate.Intent)
                .Definition
                .Keys
                .Select(key => key.Column)
                .SequenceEqual(["tree_scope", "tree_id", "left_bound"]));

        var definition = ((EnsureIndexIntent)operation.Intent).Definition;
        await SafeMigrationTestServices.IntroduceIndexDriftAsync(context, definition, "entry_depth");
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var nodesBefore = await database.ReadNodesAsync();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                [operation],
                new SafeMigrationRunOptions("nested-set-index-drift"),
                CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(context, [operation]));
        var indexesAfter = IndexSignatures(await database.ReadIndexesAsync());
        var nodesAfter = await database.ReadNodesAsync();

        // Assert
        AssertRejectedIndexDrift(report, exception, engine);
        Assert.Equal(database.Schema, definition.Schema);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(nodesBefore, nodesAfter);
    }

    /// <summary>Verifies a pending migration rejects index drift before recording an unapplied upgrade.</summary>
    /// <param name="engine">The relational database engine.</param>
    /// <param name="qualifySchema">Whether the model preserves an explicit schema or database qualifier.</param>
    /// <returns>A task that completes after verifying unchanged catalog, rows, and baseline history.</returns>
    [Theory]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task PendingUpgradeRejectsIndexDriftWithoutAdvancingHistory(
        string engine,
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine, qualifySchema);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var allOperations = GetUpOperations(upgrade, context, 1);
        var indexOperations = GetIndexOperations(upgrade, context, 1);
        var operation = indexOperations.Single(candidate => ((EnsureIndexIntent)candidate.Intent)
            .Definition
            .Keys
            .Select(key => key.Column)
            .SequenceEqual(["tree_scope", "tree_id", "left_bound"]));
        var definition = ((EnsureIndexIntent)operation.Intent).Definition;

        // WHY: The pending upgrade must classify an externally created index before EF records its history row.
        await SafeMigrationTestServices.CreateConflictingIndexAsync(context, definition, "entry_depth");
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        // WHY: The upgrade has not run; SQL Server still stores the historical Int32 coordinates.
        var nodesBefore = await database.ReadNodesAsync(MigrationStage.Baseline);
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzePendingMigrationsAsync(
                context,
                new SafeMigrationRunOptions("nested-set-pending-upgrade-drift"),
                CancellationToken.None);

        var indexesAfter = IndexSignatures(await database.ReadIndexesAsync());
        var nodesAfter = await database.ReadNodesAsync(MigrationStage.Baseline);
        var historyAfter = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        AssertUpgradeIndexOperations(engine, indexOperations);
        Assert.Equal(allOperations.Length, report.Assessments.Count);
        AssertRejectedIndexDrift(report, definition.Name);

        if (engine == "SqlServer")
        {
            // WHY: Index drift remains a blocker even when valid integer rows prove the new CHECK predicates.
            AssertApplicableIntegerChecks(report);
        }
        else if (engine != "Sqlite")
        {
            AssertOpaqueCheckConstraintBlockers(report);
        }

        Assert.Equal(database.Schema, definition.Schema);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(nodesBefore, nodesAfter);
        Assert.Equal([baseline.MigrationIds[0]], historyBefore);
        Assert.Equal(historyBefore, historyAfter);
    }

    /// <summary>Checks one drift assessment inside a complete pending-migration preflight.</summary>
    private static void AssertRejectedIndexDrift(
        SafeMigrationRunReport report,
        string indexName
    )
    {
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        var assessment = Assert.Single(
            report.Assessments,
            candidate => candidate.OperationKind == SafeMigrationOperationKind.EnsureIndex
                && candidate.ObjectName == indexName);

        Assert.Equal(SafeMigrationObservedState.Different, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.RejectDifferent, assessment.Action);
        Assert.False(assessment.PostconditionSatisfied);
        Assert.Equal("different_reject", assessment.DecisionCode);
    }

    /// <summary>Checks the shared fail-closed decision and each adapter's runtime rejection contract.</summary>
    private static void AssertRejectedIndexDrift(
        SafeMigrationRunReport report,
        Exception? exception,
        string engine
    )
    {
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        var assessment = Assert.Single(report.Assessments);
        Assert.Equal(SafeMigrationObservedState.Different, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.RejectDifferent, assessment.Action);
        Assert.False(assessment.PostconditionSatisfied);
        Assert.Equal("different_reject", assessment.DecisionCode);

        if (engine == "Sqlite")
        {
            Assert.Equal("classified_different", assessment.AnalysisCode);
            var runtimeException = Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("analysis=classified_different", runtimeException.Message, StringComparison.Ordinal);
            Assert.Contains("decision=different_reject", runtimeException.Message, StringComparison.Ordinal);
        }
        else
        {
            var databaseException = Assert.IsAssignableFrom<DbException>(exception);
            Assert.Contains("doka_sm_different", databaseException.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
