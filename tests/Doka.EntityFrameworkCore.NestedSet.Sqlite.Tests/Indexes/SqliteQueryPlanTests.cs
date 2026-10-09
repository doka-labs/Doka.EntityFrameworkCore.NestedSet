namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies SQLite native query plans using the owning provider's isolated databases.</summary>
public sealed partial class SqliteQueryPlanTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
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

        // WHY: Fresh databases have no ANALYZE statistics. Repair must still seek its 64 keys instead of
        // traversing the entire tree for every batch, as it would during a large first-time rebuild.
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(4097), true);
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
        await QueryPlanTestSupport.WriteEvidenceAsync("Sqlite-typed-repair-update", [probe.Commands[commandIndex]]);
        AssertPointKeyPlan(plan, setup.Model.FindEntityType(typeof(TreeNode))!,
            nameof(TreeNode.NodeId), nameof(TreeNode.Tree));
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
        DbContext context,
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
