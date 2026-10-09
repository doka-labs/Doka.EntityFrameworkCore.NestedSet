namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>
    /// Verifies generated SQL Server checks are accepted on an empty table with current coordinate types.
    /// </summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <returns>A task that completes after inspecting the upgraded physical contract.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerEmptyTableAcceptsGeneratedChecks(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = ScaffoldSqlServerCheckOperations(database);
        await DropSqlServerChecksAsync(context, operations);

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-empty-check-upgrade"),
                CancellationToken.None);

        await SafeMigrationTestServices.ExecuteOperationsAsync(context, operations);
        var indexes = await database.ReadIndexesAsync();
        var checks = await database.ReadChecksAsync();
        var foreignKeys = await database.ReadForeignKeysAsync();
        var columnTypes = await database.ReadColumnTypesAsync();
        var nodes = await database.ReadNodesAsync();
        var history = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        Assert.Equal(4, operations.Length);
        Assert.Equal(operations.Length, report.Assessments.Count);
        Assert.All(report.Assessments, assessment =>
        {
            Assert.Equal(SafeMigrationOperationKind.EnsureCheckConstraint, assessment.OperationKind);
            Assert.Equal(SafeMigrationObservedState.Missing, assessment.ObservedState);
            Assert.Equal(SafeMigrationAction.Apply, assessment.Action);
        });
        AssertCurrentPhysicalContract("SqlServer", indexes, checks, foreignKeys, columnTypes);
        Assert.Empty(nodes);
        Assert.Equal(chain.MigrationIds, history);
    }

    /// <summary>
    /// Verifies approved SQL Server checks replay on populated tables without catalog or row changes.
    /// </summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <returns>A task that completes after replaying and analyzing the generated check operations.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerApprovedChecksReplayOnPopulatedHierarchy(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = ScaffoldSqlServerCheckOperations(database);
        await DropSqlServerChecksAsync(context, operations);
        await SafeMigrationTestServices.ExecuteOperationsAsync(context, operations);
        await database.SeedAsync();
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var checksBefore = await database.ReadChecksAsync();
        var nodesBefore = await database.ReadNodesAsync();

        // WHY: SQL Server stamps accepted CHECK contracts; matching stamps permit replay even after data arrives.
        // Act
        await SafeMigrationTestServices.ExecuteOperationsAsync(context, operations);
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
        Assert.All(report.Assessments, assessment =>
        {
            Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
            Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
        });
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync()));
        Assert.Equal(checksBefore, await database.ReadChecksAsync());
        Assert.Equal(nodesBefore, await database.ReadNodesAsync());
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
    public async Task SqlServerNewChecksValidatePopulatedRows(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = ScaffoldSqlServerCheckOperations(database);
        await DropSqlServerChecksAsync(context, operations);
        await database.SeedAsync();
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var checksBefore = await database.ReadChecksAsync();
        var nodesBefore = await database.ReadNodesAsync();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-populated-new-checks"),
                CancellationToken.None);

        await SafeMigrationTestServices.ExecuteOperationsAsync(context, operations);
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
        Assert.All(replay.Assessments, assessment =>
        {
            Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
            Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
            Assert.True(assessment.PostconditionSatisfied);
        });
        Assert.Empty(checksBefore);
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync()));
        Assert.Equal(4, (await database.ReadChecksAsync()).Length);
        Assert.Equal(nodesBefore, await database.ReadNodesAsync());
    }

    /// <summary>Verifies SQL Server preflight approves proven integer contracts over valid historical rows.</summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <returns>A task that completes after proving unchanged rows, catalog, column types, and history.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerPopulatedUpgradePreflightApprovesIntegerContracts(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var checksBefore = await database.ReadChecksAsync();
        var nodesBefore = await database.ReadNodesAsync(MigrationStage.Baseline);
        var columnsBefore = await database.ReadColumnTypesAsync();
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
        Assert.DoesNotContain(report.Assessments, assessment => assessment.Action is
            SafeMigrationAction.RejectDifferent or SafeMigrationAction.RejectDataBlocked
            or SafeMigrationAction.RejectUnsupported);
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync()));
        Assert.Equal(checksBefore, await database.ReadChecksAsync());
        Assert.Equal(nodesBefore, await database.ReadNodesAsync(MigrationStage.Baseline));
        Assert.Equal(columnsBefore, await database.ReadColumnTypesAsync());
        Assert.Equal([baseline.MigrationIds[0]], historyBefore);
        Assert.Equal(historyBefore, await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    /// <summary>Verifies each generated SQL Server check rejects only the row that violates its predicate.</summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <param name="checkName">The suffix identifying the single violated generated check.</param>
    /// <returns>A task that completes after proving analysis and execution preserve the invalid fixture.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false, "LeftMin")]
    [InlineData(false, "RightAfterLeft")]
    [InlineData(false, "DepthMin")]
    [InlineData(false, "PositionMin")]
    [InlineData(true, "LeftMin")]
    [InlineData(true, "RightAfterLeft")]
    [InlineData(true, "DepthMin")]
    [InlineData(true, "PositionMin")]
    public async Task SqlServerInvalidRowsRejectGeneratedChecksWithoutMutation(
        bool qualifySchema,
        string checkName
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = ScaffoldSqlServerCheckOperations(database);
        await DropSqlServerChecksAsync(context, operations);
        await database.InsertNodeDirectlyAsync(CreateInvalidSqlServerNode(checkName));
        var operation = operations.Single(candidate => candidate.Intent is EnsureCheckConstraintIntent check
            && check.Definition.Name.Contains(checkName, StringComparison.Ordinal));

        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var checksBefore = await database.ReadChecksAsync();
        var nodesBefore = await database.ReadNodesAsync();
        var columnsBefore = await database.ReadColumnTypesAsync();
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-invalid-integer-check"),
                CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(context, [operation]));

        // Assert
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        Assert.Equal(4, report.Assessments.Count);
        var blocker = Assert.Single(report.Assessments, assessment => assessment.Action
            == SafeMigrationAction.RejectDataBlocked);

        Assert.Equal(((EnsureCheckConstraintIntent)operation.Intent).Definition.Name, blocker.ObjectName);
        Assert.Equal(SafeMigrationObservedState.DataBlocked, blocker.ObservedState);
        Assert.Equal("classified_data_blocked", blocker.AnalysisCode);
        Assert.Equal("data_blocked", blocker.DecisionCode);
        Assert.False(blocker.PostconditionSatisfied);
        Assert.Equal(3, report.Assessments.Count(assessment => assessment.Action == SafeMigrationAction.Apply));
        var databaseException = Assert.IsAssignableFrom<DbException>(exception);
        Assert.Contains("doka_sm_data_blocked", databaseException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync()));
        Assert.Equal(checksBefore, await database.ReadChecksAsync());
        Assert.Equal(nodesBefore, await database.ReadNodesAsync());
        Assert.Equal(columnsBefore, await database.ReadColumnTypesAsync());
        Assert.Equal(chain.MigrationIds, historyBefore);
        Assert.Equal(historyBefore, await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    /// <summary>
    /// Scaffolds real check operations for independent predicate, stamp, and enforcement qualification.
    /// </summary>
    private static SafeMigrationOperation[] ScaffoldSqlServerCheckOperations(
        MigrationDatabase database
    )
    {
        var baseline = database.Scaffold(MigrationStage.Baseline, "BeforeChecks");
        var upgrade = database.Scaffold(MigrationStage.Current, "AddChecks", baseline);
        using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);

        return GetUpOperations(upgrade, context, 1)
            .OfType<SafeMigrationOperation>()
            .Where(operation => operation.Intent is EnsureCheckConstraintIntent)
            .ToArray();
    }

    /// <summary>
    /// Creates a controlled missing-check catalog while retaining current columns, indexes, and keys.
    /// </summary>
    private static async Task DropSqlServerChecksAsync(
        DbContext context,
        IReadOnlyList<SafeMigrationOperation> operations
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();

        foreach (var operation in operations)
        {
            var definition = ((EnsureCheckConstraintIntent)operation.Intent).Definition;
            var table = sql.DelimitIdentifier(definition.Table, definition.Schema);
            var constraint = sql.DelimitIdentifier(definition.Name);
            var drop = "ALTER TABLE " + table + " DROP CONSTRAINT " + constraint;

            // WHY: Raw DDL removes only these provider-delimited test constraints; it is not a deployment path.
            await context.Database.ExecuteSqlRawAsync(drop, CancellationToken.None);
        }
    }

    /// <summary>Asserts SQL Server approves all four generated integer checks after inspecting valid rows.</summary>
    private static void AssertApplicableIntegerChecks(
        SafeMigrationRunReport report
    )
    {
        var checks = report.Assessments
            .Where(assessment => assessment.OperationKind == SafeMigrationOperationKind.EnsureCheckConstraint)
            .ToArray();

        Assert.Equal(4, checks.Length);
        Assert.All(checks, assessment =>
        {
            Assert.Equal(SafeMigrationObservedState.Missing, assessment.ObservedState);
            Assert.Equal(SafeMigrationAction.Apply, assessment.Action);
            Assert.False(assessment.PostconditionSatisfied);
        });
    }

    /// <summary>Creates a physical row that violates exactly one of the four generated integer predicates.</summary>
    private static MigrationNode CreateInvalidSqlServerNode(
        string checkName
    ) => new()
    {
        // WHY: One independent failure per fixture prevents a different CHECK from masking the selected predicate.
        NodeId = 43,
        Scope = 7,
        TreeId = 11,
        Left = checkName == "LeftMin" ? 0 : 1,
        Right = checkName == "RightAfterLeft" ? 1 : 2,
        Depth = checkName == "DepthMin" ? -1 : 0,
        Position = checkName == "PositionMin" ? -1 : 0,
        Payload = "invalid coordinate fixture",
    };
}
