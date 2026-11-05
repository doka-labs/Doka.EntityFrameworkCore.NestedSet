namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>
    /// Verifies SQL Server preflight and execution create an absent generated structural access path.
    /// </summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <returns>A task that completes after verifying the exact restored index and unchanged hierarchy rows.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerMissingIndexPreflightAppliesGeneratedAccessPath(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema,
            cancellationToken: TestContext.Current.CancellationToken);

        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain, cancellationToken: TestContext.Current.CancellationToken);
        await database.SeedAsync(cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operation = GetIndexOperations(chain, context)
            .Single(candidate => ((EnsureIndexIntent)candidate.Intent).Definition.Keys
                .Select(key => key.Column)
                .SequenceEqual(["tree_scope", "tree_id", "left_bound"]));

        var definition = ((EnsureIndexIntent)operation.Intent).Definition;
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken));

        var nodesBefore = await database.ReadNodesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var sql = context.GetService<ISqlGenerationHelper>();
        var drop = "DROP INDEX " + sql.DelimitIdentifier(definition.Name) + " ON "
            + sql.DelimitIdentifier(definition.Table, definition.Schema);

        // WHY: Removing only the provider-delimited generated index isolates the absent-index Apply contract.
        await context.Database.ExecuteSqlRawAsync(drop, CancellationToken.None);

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                [operation],
                new SafeMigrationRunOptions("nested-set-missing-index-apply"),
                CancellationToken.None);

        await SafeMigrationTestServices.ExecuteOperationsAsync(context, [operation],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        var assessment = Assert.Single(report.Assessments);
        Assert.Equal(SafeMigrationOperationKind.EnsureIndex, assessment.OperationKind);
        Assert.Equal(SafeMigrationObservedState.Missing, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.Apply, assessment.Action);
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(nodesBefore, await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Verifies identical SQL Server index columns cannot hide a changed sibling sort direction.</summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <returns>A task that completes after analysis and runtime reject the unchanged-name index drift.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerWrongIndexDirectionFailsClosedAndPreservesData(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema,
            cancellationToken: TestContext.Current.CancellationToken);

        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain, cancellationToken: TestContext.Current.CancellationToken);
        await database.SeedAsync(cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operation = GetIndexOperations(chain, context)
            .Single(candidate => ((EnsureIndexIntent)candidate.Intent).Definition.Keys
                .Select(key => key.Column)
                .SequenceEqual([
                    "tree_scope", "tree_id", "parent_entry", "entry_payload", "entry_category", "entry_id",
                ]));

        var definition = ((EnsureIndexIntent)operation.Intent).Definition;
        var original = (await database.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken)).Single(index => index.Name == definition.Name);

        var sql = context.GetService<ISqlGenerationHelper>();
        var indexName = sql.DelimitIdentifier(definition.Name);
        var table = sql.DelimitIdentifier(definition.Table, definition.Schema);
        var keys = string.Join(", ", definition.Keys.Select(key => sql.DelimitIdentifier(
            key.Column ?? throw new InvalidOperationException("The drift test requires mapped index columns."))
            + " ASC"));

        // WHY: Raw DDL changes only direction; name, ordered columns, uniqueness, and schema remain identical.
        // Identifiers cannot be SQL parameters; every identifier above is escaped by the provider helper.
        var drop = "DROP INDEX " + indexName + " ON " + table;
        var create = "CREATE INDEX " + indexName + " ON " + table + " (" + keys + ")";
        await context.Database.ExecuteSqlRawAsync(drop, CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(create, CancellationToken.None);

        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken));

        var nodesBefore = await database.ReadNodesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                [operation],
                new SafeMigrationRunOptions("nested-set-index-direction-drift"),
                CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(context, [operation],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(true, original.Descending);
        AssertRejectedIndexDrift(report, exception, "SqlServer");
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(nodesBefore, await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies SQL Server rejects disabled or untrusted stamped checks without repairing enforcement.
    /// </summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <param name="enabled">Whether the changed check is enabled again without validating existing rows.</param>
    /// <returns>
    /// A task that completes after proving the check's enforcement flags, rows, and indexes are unchanged.
    /// </returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SqlServerUnenforcedCheckFailsClosedWithoutRepair(
        bool qualifySchema,
        bool enabled
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema,
            cancellationToken: TestContext.Current.CancellationToken);

        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(chain, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = ScaffoldSqlServerCheckOperations(database);
        await DropSqlServerChecksAsync(context, operations, cancellationToken: TestContext.Current.CancellationToken);
        await SafeMigrationTestServices.ExecuteOperationsAsync(context, operations,
            cancellationToken: TestContext.Current.CancellationToken);
        await database.SeedAsync(cancellationToken: TestContext.Current.CancellationToken);
        var operation = operations
            .Single(candidate => candidate.Intent is EnsureCheckConstraintIntent check
                && check.Definition.Name.Contains("LeftMin", StringComparison.Ordinal));

        var definition = ((EnsureCheckConstraintIntent)operation.Intent).Definition;
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(definition.Table, definition.Schema);
        var constraint = sql.DelimitIdentifier(definition.Name);

        // WHY: The contract stamp and expression remain intact; disabled or untrusted enforcement is still drift.
        // These model identifiers are provider-delimited because SQL Server cannot parameterize DDL identifiers.
        var disable = "ALTER TABLE " + table + " NOCHECK CONSTRAINT " + constraint;
        await context.Database.ExecuteSqlRawAsync(disable, CancellationToken.None);

        if (enabled)
        {
            // WHY: CHECK without WITH CHECK reenables enforcement but leaves the existing-row contract untrusted.
            var enable = "ALTER TABLE " + table + " CHECK CONSTRAINT " + constraint;
            await context.Database.ExecuteSqlRawAsync(enable, CancellationToken.None);
        }

        var stateBefore = await ReadSqlServerCheckStateAsync(context, definition,
            cancellationToken: TestContext.Current.CancellationToken);

        var checksBefore = await database.ReadChecksAsync(cancellationToken: TestContext.Current.CancellationToken);
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken));

        var nodesBefore = await database.ReadNodesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                [operation],
                new SafeMigrationRunOptions("nested-set-disabled-check-drift"),
                CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(context, [operation],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        var assessment = Assert.Single(report.Assessments);
        Assert.Equal(SafeMigrationOperationKind.EnsureCheckConstraint, assessment.OperationKind);
        Assert.Equal(SafeMigrationObservedState.Different, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.RejectDifferent, assessment.Action);
        Assert.Equal("different_reject", assessment.DecisionCode);
        Assert.False(assessment.PostconditionSatisfied);
        var databaseException = Assert.IsAssignableFrom<DbException>(exception);
        Assert.Contains("doka_sm_different", databaseException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(enabled ? 1 : 3, stateBefore);
        Assert.Equal(stateBefore, await ReadSqlServerCheckStateAsync(context, definition,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(checksBefore, await database.ReadChecksAsync(
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(nodesBefore, await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Captures disabled and untrusted flags independently of a check's unchanged authored expression.
    /// </summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="definition">The expected physical index definition.</param>
    private static Task<int> ReadSqlServerCheckStateAsync(
        DbContext context,
        ExpectedCheckConstraintDefinition definition,
        CancellationToken cancellationToken
    )
    {
        var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(definition.Table, definition.Schema);

        return context.Database.SqlQuery<int>($"""
            SELECT CAST(is_disabled AS int) * 2 + CAST(is_not_trusted AS int) AS [Value]
            FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID({table}) AND name = {definition.Name}
            """).SingleAsync(cancellationToken);
    }
}
