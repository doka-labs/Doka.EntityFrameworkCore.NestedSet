namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>Generated partial parent indexes replay as matching objects without changing rows or history.</summary>
    /// <param name="qualifySchema">Whether the physical hierarchy uses an explicit schema.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostgreSqlPartialParentIndexesReplayExecutionWithoutMutation(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("PostgreSql", qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "PartialParents");
        await database.MigrateAsync(chain);
        await database.SeedAsync();
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = GetIndexOperations(chain, context)
            .Where(operation => ((EnsureIndexIntent)operation.Intent).Definition.Filter
                is "parent_entry IS NULL" or "parent_entry IS NOT NULL").ToArray();
        var filtersBefore = await ReadPostgreSqlParentFiltersAsync(context);
        var nodesBefore = await database.ReadNodesAsync();
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        await SafeMigrationTestServices.ExecuteOperationsAsync(context, operations);

        // Assert
        var filtersAfter = await ReadPostgreSqlParentFiltersAsync(context);
        var nodesAfter = await database.ReadNodesAsync();
        var historyAfter = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        Assert.Equal(2, operations.Length);
        Assert.Equal(6, filtersBefore.Count);
        Assert.Equal(4, filtersBefore.Values.Count(filter => NormalizeSql(filter) == "tree_idisnotnull"));
        Assert.All(operations, operation =>
        {
            var definition = ((EnsureIndexIntent)operation.Intent).Definition;
            Assert.Equal(NormalizeSql(definition.Filter!), NormalizeSql(filtersBefore[definition.Name]));
            Assert.False(definition.Unique);
        });
        Assert.Equal(filtersBefore.OrderBy(pair => pair.Key), filtersAfter.OrderBy(pair => pair.Key));
        Assert.Equal(nodesBefore, nodesAfter);
        Assert.Equal(historyBefore, historyAfter);
    }

    /// <summary>Generated partial parent indexes analyze as matching objects without changing persistent state.</summary>
    /// <param name="qualifySchema">Whether the physical hierarchy uses an explicit schema.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostgreSqlPartialParentIndexesAnalyzeWithoutMutation(
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("PostgreSql", qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "PartialParents");
        await database.MigrateAsync(chain);
        await database.SeedAsync();
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var operations = GetIndexOperations(chain, context)
            .Where(operation => ((EnsureIndexIntent)operation.Intent).Definition.Filter
                is "parent_entry IS NULL" or "parent_entry IS NOT NULL").ToArray();
        var filtersBefore = await ReadPostgreSqlParentFiltersAsync(context);
        var nodesBefore = await database.ReadNodesAsync();
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context.GetService<ISafeMigrationRunner>().AnalyzeAsync(context, operations,
            new SafeMigrationRunOptions("nested-set-partial-parent-replay"), CancellationToken.None);

        // Assert
        var filtersAfter = await ReadPostgreSqlParentFiltersAsync(context);
        var nodesAfter = await database.ReadNodesAsync();
        var historyAfter = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        Assert.Equal(2, operations.Length);
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        Assert.Equal(2, report.Assessments.Count);
        Assert.All(report.Assessments, assessment =>
        {
            Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
            Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
        });
        Assert.Equal(filtersBefore.OrderBy(pair => pair.Key), filtersAfter.OrderBy(pair => pair.Key));
        Assert.Equal(nodesBefore, nodesAfter);
        Assert.Equal(historyBefore, historyAfter);
    }

    /// <summary>
    /// A same-name hierarchy index with the opposite filter fails closed without catalog or data repair.
    /// </summary>
    /// <param name="qualifySchema">Whether the physical hierarchy uses an explicit schema.</param>
    /// <param name="role">The dependent, root, or generated tree index to corrupt.</param>
    [Theory]
    [InlineData(false, "dependent")]
    [InlineData(false, "roots")]
    [InlineData(false, "tree")]
    [InlineData(true, "dependent")]
    [InlineData(true, "roots")]
    [InlineData(true, "tree")]
    public async Task PostgreSqlPartialHierarchyFilterDriftAnalysisIsRejectedWithoutMutation(
        bool qualifySchema,
        string role
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("PostgreSql", qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "PartialParents");
        await database.MigrateAsync(chain);
        await database.SeedAsync();
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var filter = role switch
        {
            "roots" => "parent_entry IS NULL", "dependent" => "parent_entry IS NOT NULL", _ => "tree_id IS NOT NULL",
        };
        var operation = GetIndexOperations(chain, context)
            .Single(candidate => ((EnsureIndexIntent)candidate.Intent).Definition.Filter == filter
                && (role != "tree" || ((EnsureIndexIntent)candidate.Intent).Definition.Keys
                    .Select(key => key.Column).SequenceEqual(["tree_scope", "tree_id", "left_bound"])));
        var definition = ((EnsureIndexIntent)operation.Intent).Definition;
        var sql = context.GetService<ISqlGenerationHelper>();
        var name = sql.DelimitIdentifier(definition.Name, definition.Schema);
        var table = sql.DelimitIdentifier(definition.Table, definition.Schema);
        var columns = string.Join(", ", definition.Keys.Select(key => sql.DelimitIdentifier(
            key.Column ?? throw new InvalidOperationException("Parent index keys must use physical columns."))));
        var wrongFilter = role switch
        {
            "roots" => "parent_entry IS NOT NULL", "dependent" => "parent_entry IS NULL", _ => "tree_id IS NULL",
        };
        var drop = "DROP INDEX " + name;
        var create = "CREATE INDEX " + sql.DelimitIdentifier(definition.Name) + " ON " + table
            + " (" + columns + ") WHERE " + wrongFilter;

        // WHY: Native DDL represents external drift independently of the generated migration's safeguards.
        await context.Database.ExecuteSqlRawAsync(drop, CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(create, CancellationToken.None);
        var filtersBefore = await ReadPostgreSqlParentFiltersAsync(context);
        var nodesBefore = await database.ReadNodesAsync();
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context.GetService<ISafeMigrationRunner>().AnalyzeAsync(context, [operation],
            new SafeMigrationRunOptions("nested-set-partial-parent-drift"), CancellationToken.None);

        // Assert
        var filtersAfter = await ReadPostgreSqlParentFiltersAsync(context);
        var nodesAfter = await database.ReadNodesAsync();
        var historyAfter = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        Assert.Single(report.Assessments);
        AssertRejectedIndexDrift(report, definition.Name);
        Assert.Equal(NormalizeSql(wrongFilter), NormalizeSql(filtersBefore[definition.Name]));
        Assert.Equal(filtersBefore.OrderBy(pair => pair.Key), filtersAfter.OrderBy(pair => pair.Key));
        Assert.Equal(nodesBefore, nodesAfter);
        Assert.Equal(historyBefore, historyAfter);
    }

    /// <summary>A same-name hierarchy index with the opposite filter rejects execution without mutation.</summary>
    /// <param name="qualifySchema">Whether the physical hierarchy uses an explicit schema.</param>
    /// <param name="role">The dependent, root, or generated tree index to corrupt.</param>
    [Theory]
    [InlineData(false, "dependent")]
    [InlineData(false, "roots")]
    [InlineData(false, "tree")]
    [InlineData(true, "dependent")]
    [InlineData(true, "roots")]
    [InlineData(true, "tree")]
    public async Task PostgreSqlPartialHierarchyFilterDriftExecutionIsRejectedWithoutMutation(
        bool qualifySchema,
        string role
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("PostgreSql", qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "PartialParents");
        await database.MigrateAsync(chain);
        await database.SeedAsync();
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var filter = role switch
        {
            "roots" => "parent_entry IS NULL", "dependent" => "parent_entry IS NOT NULL", _ => "tree_id IS NOT NULL",
        };
        var operation = GetIndexOperations(chain, context)
            .Single(candidate => ((EnsureIndexIntent)candidate.Intent).Definition.Filter == filter
                && (role != "tree" || ((EnsureIndexIntent)candidate.Intent).Definition.Keys
                    .Select(key => key.Column).SequenceEqual(["tree_scope", "tree_id", "left_bound"])));
        var definition = ((EnsureIndexIntent)operation.Intent).Definition;
        var sql = context.GetService<ISqlGenerationHelper>();
        var name = sql.DelimitIdentifier(definition.Name, definition.Schema);
        var table = sql.DelimitIdentifier(definition.Table, definition.Schema);
        var columns = string.Join(", ", definition.Keys.Select(key => sql.DelimitIdentifier(
            key.Column ?? throw new InvalidOperationException("Parent index keys must use physical columns."))));
        var wrongFilter = role switch
        {
            "roots" => "parent_entry IS NOT NULL", "dependent" => "parent_entry IS NULL", _ => "tree_id IS NULL",
        };
        var drop = "DROP INDEX " + name;
        var create = "CREATE INDEX " + sql.DelimitIdentifier(definition.Name) + " ON " + table
            + " (" + columns + ") WHERE " + wrongFilter;

        // WHY: Native DDL represents external drift independently of the generated migration's safeguards.
        await context.Database.ExecuteSqlRawAsync(drop, CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(create, CancellationToken.None);
        var filtersBefore = await ReadPostgreSqlParentFiltersAsync(context);
        var nodesBefore = await database.ReadNodesAsync();
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(context, [operation]));

        // Assert
        var filtersAfter = await ReadPostgreSqlParentFiltersAsync(context);
        var nodesAfter = await database.ReadNodesAsync();
        var historyAfter = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        var databaseException = Assert.IsAssignableFrom<DbException>(exception);
        Assert.Contains("doka_sm_different", databaseException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NormalizeSql(wrongFilter), NormalizeSql(filtersBefore[definition.Name]));
        Assert.Equal(filtersBefore.OrderBy(pair => pair.Key), filtersAfter.OrderBy(pair => pair.Key));
        Assert.Equal(nodesBefore, nodesAfter);
        Assert.Equal(historyBefore, historyAfter);
    }

    /// <summary>Reads actual partial-index predicates without modifying statistics or catalog metadata.</summary>
    private static async Task<Dictionary<string, string>> ReadPostgreSqlParentFiltersAsync(
        DbContext context
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT i.relname, pg_get_expr(ix.indpred, ix.indrelid)
            FROM pg_index ix JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace JOIN pg_class i ON i.oid = ix.indexrelid
            WHERE t.relname = @table AND n.nspname = @schema AND ix.indpred IS NOT NULL
            ORDER BY i.relname
            """;
        var table = command.CreateParameter();
        table.ParameterName = "table";
        table.Value = MigrationContext.TableName;
        command.Parameters.Add(table);
        var schema = command.CreateParameter();
        schema.ParameterName = "schema";
        schema.Value = context.Model.GetDefaultSchema() ?? "public";
        command.Parameters.Add(schema);
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            filters.Add(reader.GetString(0), reader.GetString(1));
        }

        return filters;
    }
}
