namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>
    /// Verifies approved SQL Server checks replay on populated tables without catalog or row changes.
    /// </summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <returns>A task that completes after replaying and analyzing the generated check operations.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteOperationsAsync_SqlServerApprovedChecks_ReplayOnPopulatedHierarchy(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(
            "SqlServer",
            qualifySchema,
            cancellationToken: TestContext.Current.CancellationToken);

        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain, cancellationToken: TestContext.Current.CancellationToken, target: null);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = ScaffoldSqlServerCheckOperations(database);
        await DropSqlServerChecksAsync(context, operations, cancellationToken: TestContext.Current.CancellationToken);
        await SafeMigrationTestServices.ExecuteOperationsAsync(
            context,
            operations,
            cancellationToken: TestContext.Current.CancellationToken);

        await database.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var indexesBefore = IndexSignatures(
            await database.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var checksBefore = await database.ReadChecksAsync(cancellationToken: TestContext.Current.CancellationToken);
        var nodesBefore = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        // WHY: SQL Server stamps accepted CHECK contracts; matching stamps permit replay even after data arrives.
        // Act
        await SafeMigrationTestServices.ExecuteOperationsAsync(
            context,
            operations,
            cancellationToken: TestContext.Current.CancellationToken);

        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-approved-check-replay"),
                CancellationToken.None);

        // Assert
        Assert.Equal(4, operations.Length);
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        Assert.Equal(operations.Length, report.Assessments.Count);
        Assert.All(
            report.Assessments,
            assessment =>
            {
                Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
                Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
            });

        Assert.Equal(
            indexesBefore,
            IndexSignatures(
                await database.ReadIndexesAsync(
                    cancellationToken: TestContext.Current.CancellationToken)));

        Assert.Equal(
            checksBefore,
            await database.ReadChecksAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            nodesBefore,
            await database.ReadNodesAsync(
                cancellationToken: TestContext.Current.CancellationToken,
                stage: MigrationStage.Current));
    }

    /// <summary>
    /// Verifies generated SQL Server integer checks validate existing hierarchy rows before creation.
    /// </summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <returns>A task that completes after applying the missing checks without changing rows or indexes.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnalyzeAsync_SqlServerNewChecks_ValidatePopulatedRows(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(
            "SqlServer",
            qualifySchema,
            cancellationToken: TestContext.Current.CancellationToken);

        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain, cancellationToken: TestContext.Current.CancellationToken, target: null);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = ScaffoldSqlServerCheckOperations(database);
        await DropSqlServerChecksAsync(context, operations, cancellationToken: TestContext.Current.CancellationToken);
        await database.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var indexesBefore = IndexSignatures(
            await database.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var checksBefore = await database.ReadChecksAsync(cancellationToken: TestContext.Current.CancellationToken);
        var nodesBefore = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-populated-new-checks"),
                CancellationToken.None);

        await SafeMigrationTestServices.ExecuteOperationsAsync(
            context,
            operations,
            cancellationToken: TestContext.Current.CancellationToken);

        // WHY: Matching proves populated-table creation retained its stamp and enabled, trusted enforcement.
        var replay = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-populated-check-replay"),
                CancellationToken.None);

        // Assert
        Assert.Equal(4, operations.Length);
        Assert.Equal(operations.Length, report.Assessments.Count);
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        AssertApplicableIntegerChecks(report);
        Assert.Equal(SafeMigrationReportStatus.Ready, replay.Status);
        Assert.Equal(operations.Length, replay.Assessments.Count);
        Assert.All(
            replay.Assessments,
            assessment =>
            {
                Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
                Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
                Assert.True(assessment.PostconditionSatisfied);
            });

        Assert.Empty(checksBefore);
        Assert.Equal(
            indexesBefore,
            IndexSignatures(
                await database.ReadIndexesAsync(
                    cancellationToken: TestContext.Current.CancellationToken)));

        Assert.Equal(
            4,
            (await database.ReadChecksAsync(
                cancellationToken: TestContext.Current.CancellationToken)).Length);

        Assert.Equal(
            nodesBefore,
            await database.ReadNodesAsync(
                cancellationToken: TestContext.Current.CancellationToken,
                stage: MigrationStage.Current));
    }

    /// <summary>Verifies SQL Server preflight approves proven integer contracts over valid historical rows.</summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <returns>A task that completes after proving unchanged rows, catalog, column types, and history.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnalyzePendingMigrationsAsync_SqlServerPopulatedUpgradePreflight_ApprovesIntegerContracts(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(
            "SqlServer",
            qualifySchema,
            cancellationToken: TestContext.Current.CancellationToken);

        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline, cancellationToken: TestContext.Current.CancellationToken, target: null);
        await database.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var indexesBefore = IndexSignatures(
            await database.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var checksBefore = await database.ReadChecksAsync(cancellationToken: TestContext.Current.CancellationToken);
        var nodesBefore = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);

        var columnsBefore = await database.ReadColumnTypesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzePendingMigrationsAsync(
                context,
                new SafeMigrationRunOptions("nested-set-populated-check-preflight"),
                CancellationToken.None);

        // Assert
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        AssertApplicableIntegerChecks(report);
        Assert.DoesNotContain(
            report.Assessments,
            assessment => assessment.Action is
                SafeMigrationAction.RejectDifferent
                or SafeMigrationAction.RejectDataBlocked
                or SafeMigrationAction.RejectUnsupported);

        Assert.Equal(
            indexesBefore,
            IndexSignatures(
                await database.ReadIndexesAsync(
                    cancellationToken: TestContext.Current.CancellationToken)));

        Assert.Equal(
            checksBefore,
            await database.ReadChecksAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            nodesBefore,
            await database.ReadNodesAsync(
                cancellationToken: TestContext.Current.CancellationToken,
                stage: MigrationStage.Baseline));

        Assert.Equal(
            columnsBefore,
            await database.ReadColumnTypesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([baseline.MigrationIds[0]], historyBefore);
        Assert.Equal(historyBefore, await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }
}
