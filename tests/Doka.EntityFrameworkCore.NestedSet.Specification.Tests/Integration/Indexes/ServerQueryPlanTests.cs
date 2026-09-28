namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Captures real server plans for scoped wide and deep hierarchy queries.</summary>
public abstract class ServerQueryPlanTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Uses isolated ordering tables on each supported server.</summary>
    protected ServerQueryPlanTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    /// Native plans preserve selective scoped anchors and expose the cost of ancestor and ordering scans.
    /// </summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "The assertion requires a server database and server execution plans rather than a SQLite file.")]
    public async Task ScopedHierarchyQueriesHaveServerPlanEvidence()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        await RefreshStatisticsAsync<OrderingNode>(setup, Engine);
        var probe = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var plans = new List<string>();
        var counts = new List<int>();

        // Act
        foreach (var scope in new[] { 10, 11 })
        {
            var service = context
                .NestedSet<OrderingNode>()
                .ForScope(scope);

            foreach (var offset in new[] { 0, 100, 199 })
            {
                var key = (scope * 1000) + offset;
                var queries = new[] { service.ParentOf(key), service.AncestorsOf(key), service.DescendantsOf(key) };

                for (var queryIndex = 0; queryIndex < queries.Length; queryIndex++)
                {
                    var commandIndex = probe.Commands.Count;
                    var rows = await queries[queryIndex]
                        .Select(node => node.Id)
                        .ToArrayAsync(CancellationToken.None);

                    counts.Add(rows.Length);
                    var plan = await ExplainAsync(context, probe, commandIndex, Engine);
                    plans.Add($"scope={scope}; offset={offset}; query={queryIndex}; rows={rows.Length}\n{plan}");
                }
            }
        }

        var orderingIndex = probe.Commands.Count;
        var store = new NestedSetStore<OrderingNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(OrderingNode))!,
            10,
            Guid.Empty);

        await new NestedSetOrderer<OrderingNode, int, Guid, int>(store).ResolveAsync(
            10100,
            new NestedSetParent<int>(true, 10000),
            CancellationToken.None);
        plans.Add("configured-predecessor\n" + await ExplainAsync(context, probe, orderingIndex + 1, Engine));
        await QueryPlanTestSupport.WriteEvidenceAsync(Engine + "-hierarchy", plans);

        // Assert
        Assert.Equal([0, 0, 199, 1, 1, 0, 1, 1, 0, 0, 0, 199, 1, 100, 99, 1, 199, 0], counts);
        Assert.All(plans, Assert.NotEmpty);

        // WHY: Empty root-ancestor results may be proven without reading an index. Nonempty direct-parent
        // point lookups must retain index evidence; wider interval and ordering plans remain inspectable artifacts.
        AssertPointLookups(Engine, plans[3]);
        AssertPointLookups(Engine, plans[12]);
        Assert.All(probe.Commands, sql => Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gives the optimizer current seed distributions without waiting for background statistics collection.
    /// </summary>
    internal static Task RefreshStatisticsAsync<TEntity>(
        DbContext context,
        string engine
    )
        where TEntity : class
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        // WHY: Fresh PostgreSQL tables otherwise estimated two scope rows where the probe seeded two hundred.
        // Keep statistics preparation outside measured queries and restrict it to this case's owned table.
        var statement = engine switch
        {
            "SqlServer" => "UPDATE STATISTICS " + table + " WITH FULLSCAN",
            "MySql" or "MariaDb" => "ANALYZE TABLE " + table,
            _ => "ANALYZE " + table,
        };

        return context.Database.ExecuteSqlRawAsync(statement, CancellationToken.None);
    }

    /// <summary>
    /// Checks selective native access paths even when the optimizer resolves point reads as constants.
    /// </summary>
    private static void AssertPointLookups(
        string engine,
        string plan
    )
    {
        if (engine is not ("MySql" or "MariaDb"))
        {
            Assert.Contains("index", plan, StringComparison.OrdinalIgnoreCase);

            return;
        }

        var jsonStart = plan.IndexOf('{', StringComparison.Ordinal);
        Assert.True(jsonStart >= 0, plan);
        var reader = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(plan[jsonStart..]));
        using var document = System.Text.Json.JsonDocument.ParseValue(ref reader);
        var tables = Objects(document.RootElement)
            .Where(value => value.TryGetProperty("table_name", out _) && value.TryGetProperty("access_type", out _))
            .ToArray();

        Assert.Equal(2, tables.Length);
        Assert.All(
            tables,
            table =>
            {
                Assert.Equal("PRIMARY", table.GetProperty("key").GetString());
                Assert.True(table.GetProperty("access_type").GetString() is "const" or "eq_ref", plan);
                var rows = table.TryGetProperty("rows_examined_per_scan", out var estimated)
                    ? estimated
                    : table.GetProperty("rows");

                Assert.InRange(rows.GetDouble(), 0, 1);
            });
    }

    /// <summary>Walks provider plan objects without relying on a particular nested-loop wrapper shape.</summary>
    private static IEnumerable<System.Text.Json.JsonElement> Objects(
        System.Text.Json.JsonElement value
    )
    {
        if (value.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            yield return value;

            foreach (var property in value.EnumerateObject())
            {
                foreach (var nested in Objects(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (value.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var nested in Objects(item))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Obtains actual execution plans while retaining the mapped parameter values of the original query.
    /// </summary>
    internal static async Task<string> ExplainAsync(
        DbContext context,
        EnterpriseProbe probe,
        int index,
        string engine
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        var sql = probe.Commands[index];
        command.CommandText = engine switch
        {
            "PostgreSql" => "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql,
            // WHY: Const lookups disappear from MySQL's runtime TREE once fetched during optimization.
            // Retain JSON access-path metadata alongside the actual runtime plan to verify those primary-key reads.
            "MySql" => "EXPLAIN FORMAT=JSON " + sql + "; EXPLAIN ANALYZE FORMAT=TREE " + sql,
            "MariaDb" => "ANALYZE FORMAT=JSON " + sql,
            "SqlServer" => "SET STATISTICS XML ON; " + sql + "; SET STATISTICS XML OFF;",
            _ => "EXPLAIN QUERY PLAN " + sql,
        };

        foreach (var parameter in probe.ParameterBindings[index])
        {
            command.Parameters.Add(parameter);
        }

        var plan = new System.Text.StringBuilder();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        do
        {
            while (await reader.ReadAsync(CancellationToken.None))
            {
                // WHY: SQL Server first returns the actual SELECT results, then a separate XML plan result set.
                if (engine == "SqlServer"
                    && !reader
                        .GetName(0)
                        .Contains("Showplan", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                plan.AppendLine(reader.GetString(engine == "Sqlite" ? 3 : 0));
            }
        } while (await reader.NextResultAsync(CancellationToken.None));

        return plan.ToString();
    }

    /// <summary>
    /// Seeds twenty overlapping scopes, alternating wide siblings and deep chains, without timed inserts.
    /// </summary>
    private static async Task SeedAsync(
        OrderingContext context
    )
    {
        for (var scope = 1; scope <= 20; scope++)
        {
            for (var offset = 0; offset < 200; offset++)
            {
                var root = scope * 1000;
                var wide = scope % 2 == 0;
                await context.AddAsync(
                    new OrderingNode
                    {
                        Id = root + offset,
                        Scope = scope,
                        Name = $"Node-{offset:D4}",
                        ParentId = offset == 0
                            ? null
                            : wide
                                ? root
                                : root + offset - 1,
                        Left = wide ? offset == 0 ? 1 : offset * 2 : offset + 1,
                        Right = wide ? offset == 0 ? 400 : (offset * 2) + 1 : 400 - offset,
                        Depth = wide ? offset == 0 ? 0 : 1 : offset,
                        Position = wide && offset != 0 ? offset - 1 : 0,
                    },
                    CancellationToken.None);
            }
        }

        using (NestedSetSaveChanges.EnterManagedSave(
                   context,
                   context
                       .ChangeTracker
                       .Entries()
                       .Select(entry => entry.Entity),
                   static () => { }))
        {
            await context.SaveChangesAsync(CancellationToken.None);
        }

        context.ChangeTracker.Clear();
    }
}
