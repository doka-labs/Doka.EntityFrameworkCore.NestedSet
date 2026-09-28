namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies SQLite native query plans using the owning provider's isolated databases.</summary>
public sealed class SqliteQueryPlanTests : ProviderTest, IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses this suite's isolated SQLite fixture for native plan assertions.</summary>
    /// <param name="fixture">The exact provider-bound fixture owned by this suite.</param>
    public SqliteQueryPlanTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    ///     Verifies that repair batches seek individual keys instead of scanning every row in their scope.
    /// </summary>
    /// <returns>A task that completes after explaining the actual repair UPDATE with its original parameters.</returns>
    [Fact]
    public async Task RepairBatchQueryPlanUsesScopeAndNodeKeyConstraint()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();

        // WHY: A 64-row repair batch must be selective within its tree; tiny trees can be cheaper to scan in full.
        // Real statistics let SQLite compare the keyed lookup with the exact-tree index on representative data.
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(4097), true);
        await setup.Database.ExecuteSqlRawAsync("ANALYZE", CancellationToken.None);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        await tree.RebuildAsync(CancellationToken.None);

        // Assert
        Assert.Equal(65, probe.NodeUpdates);
        var commandIndex = probe.Commands.FindIndex(command => command.StartsWith("UPDATE", StringComparison.Ordinal)
            && command.Contains(nameof(TreeNode), StringComparison.Ordinal));

        Assert.InRange(commandIndex, 0, probe.Commands.Count - 1);
        var plan = await ExplainAsync(setup, probe, commandIndex);
        await QueryPlanTestSupport.WriteEvidenceAsync("Sqlite-typed-repair-plan", plan);
        var predicate = $"{nameof(TreeNode.Tree)}=? AND {nameof(TreeNode.NodeId)}=?";

        // WHY: SQLite backs the required alternate key with an implementation-named auto-index. The access
        // predicate is the stable contract; asserting an EF constraint name would not describe the physical plan.
        Assert.Contains(
            plan,
            detail =>
                (detail.Contains("USING INDEX", StringComparison.Ordinal)
                    && detail.Contains(predicate, StringComparison.Ordinal))
                || (detail.Contains("USING INTEGER PRIMARY KEY", StringComparison.Ordinal)
                    && detail.Contains("rowid=?", StringComparison.Ordinal)));
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN ", StringComparison.Ordinal));
    }

    /// <summary>SQLite resolves the parent by its primary key and descendants through a bounded left index.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlansUseIndexedParentOrDescendantLookup(
        bool parentQuery
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var query = parentQuery ? service.ParentOf(3) : service.DescendantsOf(1);

        // Act
        await query.ToArrayAsync(CancellationToken.None);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "EXPLAIN QUERY PLAN " + probe.Commands[0];

        for (var index = 0; index < probe.ParameterNames[0].Length; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = probe.ParameterNames[0][index];
            parameter.Value = probe.ParameterValues[0][index];
            command.Parameters.Add(parameter);
        }

        var steps = new List<string>();

        await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
        {
            while (await reader.ReadAsync(CancellationToken.None))
            {
                steps.Add(reader.GetString(3));
            }
        }

        // Assert
        Assert.DoesNotContain(steps, step => step.Contains("CORRELATED", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(steps, step => step.Contains("INTEGER PRIMARY KEY", StringComparison.OrdinalIgnoreCase));

        if (!parentQuery)
        {
            var entity = context.Model.FindEntityType(typeof(TreeNode))!;
            var leftIndex = entity
                .GetIndexes()
                .Single(index => index
                    .Properties
                    .Select(property => property.Name)
                    .SequenceEqual([nameof(TreeNode.Tree), nameof(TreeNode.TreeId), nameof(TreeNode.Start)]));

            var indexName = leftIndex.GetDatabaseName();
            Assert.Contains(
                steps,
                step => step.Contains(indexName!, StringComparison.Ordinal)
                    && step.Contains("TreeId=?", StringComparison.OrdinalIgnoreCase)
                    && step.Contains("Start>?", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Explains an observed SQLite command without executing its writes a second time.</summary>
    private static async Task<List<string>> ExplainAsync(
        TreeContext context,
        EnterpriseProbe probe,
        int commandIndex
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "EXPLAIN QUERY PLAN " + probe.Commands[commandIndex];
        var names = probe.ParameterNames[commandIndex];
        var values = probe.ParameterValues[commandIndex];

        for (var index = 0; index < names.Length; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = names[index];
            parameter.Value = values[index] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            details.Add(reader.GetString(3));
        }

        return details;
    }
}
