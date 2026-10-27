using System.Text;
using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Captures real server plans for independently discovered scoped hierarchy query contracts.</summary>
public abstract class ServerQueryPlanTests : ProviderTest
{
    private readonly ServerQueryPlanFixture _fixture;

    /// <summary>Shares one immutable ordering dataset across this concrete provider suite's query cases.</summary>
    protected ServerQueryPlanTests(
        IProviderFixture<ServerQueryPlanFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Gets each original scope, anchor, query kind and independent expected row count.</summary>
    public static TheoryData<int, int, string, int> HierarchyQueryCases =>
        new()
        {
            { 10, 0, "Parent", 0 },
            { 10, 0, "Ancestors", 0 },
            { 10, 0, "Descendants", 199 },
            { 10, 100, "Parent", 1 },
            { 10, 100, "Ancestors", 1 },
            { 10, 100, "Descendants", 0 },
            { 10, 199, "Parent", 1 },
            { 10, 199, "Ancestors", 1 },
            { 10, 199, "Descendants", 0 },
            { 11, 0, "Parent", 0 },
            { 11, 0, "Ancestors", 0 },
            { 11, 0, "Descendants", 199 },
            { 11, 100, "Parent", 1 },
            { 11, 100, "Ancestors", 100 },
            { 11, 100, "Descendants", 99 },
            { 11, 199, "Parent", 1 },
            { 11, 199, "Ancestors", 199 },
            { 11, 199, "Descendants", 0 },
        };

    /// <summary>One native plan preserves its independently named scope, anchor and hierarchy query contract.</summary>
    /// <param name="scope">The wide or deep hierarchy partition.</param>
    /// <param name="offset">The root, middle or final anchor offset.</param>
    /// <param name="query">The parent, ancestor or descendant query kind.</param>
    /// <param name="expectedRows">The original independent expected result count.</param>
    [EngineTheory(
        ExcludedEngines = ["Sqlite"],
        Reason = "The assertion requires a server database and server execution plans rather than a SQLite file.")]
    [MemberData(nameof(HierarchyQueryCases))]
    public async Task ScopedHierarchyQueriesHaveServerPlanEvidence(
        int scope,
        int offset,
        string query,
        int expectedRows
    )
    {
        // Arrange
        var probe = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await _fixture.CreateContextAsync(Engine, probe);
        var service = context
            .NestedSet<OrderingNode>()
            .ForScope(scope);

        var key = scope * 1000 + offset;
        var selected = query switch
        {
            "Parent" => service.ParentOf(key),
            "Ancestors" => service.AncestorsOf(key),
            "Descendants" => service.DescendantsOf(key),
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
        };

        // Act
        var commandIndex = probe.Commands.Count;
        var rows = await selected
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var plan = await QueryPlanTestSupport.ExplainAsync(context, probe, commandIndex, Engine);
        var queryIndex = query switch
        {
            "Parent" => 0,
            "Ancestors" => 1,
            _ => 2,
        };

        _fixture.RecordEvidence(
            scope,
            offset,
            query,
            $"scope={scope}; offset={offset}; query={queryIndex}; rows={rows.Length}\n{plan}");

        // Assert
        Assert.Equal(expectedRows, rows.Length);
        Assert.NotEmpty(plan);

        if (query == "Parent"
            && offset == 100)
        {
            // WHY: These are the two original selective point-lookup controls; root ancestors may be constant-empty.
            AssertPointLookups(Engine, plan);
        }

        Assert.All(probe.Commands, sql => Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Configured predecessor resolution retains its own native ordering-plan evidence.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "The assertion requires a server database and server execution plans rather than a SQLite file.")]
    public async Task ConfiguredPredecessorHasServerPlanEvidence()
    {
        // Arrange
        var probe = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await _fixture.CreateContextAsync(Engine, probe);
        var store = new NestedSetStore<OrderingNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(OrderingNode))!,
            10,
            Guid.Empty);

        // Act
        var commandIndex = probe.Commands.Count;
        await new NestedSetOrderer<OrderingNode, int, Guid, int>(store).ResolveAsync(
            10100,
            new NestedSetParent<int>(true, 10000),
            CancellationToken.None);

        var plan = await QueryPlanTestSupport.ExplainAsync(context, probe, commandIndex + 1, Engine);
        _fixture.RecordPredecessorEvidence(plan);

        // Assert
        Assert.NotEmpty(plan);
        Assert.All(probe.Commands, sql => Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase));
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
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(plan[jsonStart..]));
        using var document = JsonDocument.ParseValue(ref reader);
        var tables = Objects(document.RootElement)
            .Where(value => value.TryGetProperty("table_name", out _) && value.TryGetProperty("access_type", out _))
            .ToArray();

        Assert.Equal(2, tables.Length);
        Assert.All(
            tables,
            table =>
            {
                Assert.Equal(
                    "PRIMARY",
                    table
                        .GetProperty("key")
                        .GetString());
                Assert.True(
                    table
                        .GetProperty("access_type")
                        .GetString() is "const" or "eq_ref",
                    plan);
                var rows = table.TryGetProperty("rows_examined_per_scan", out var estimated)
                    ? estimated
                    : table.GetProperty("rows");

                Assert.InRange(rows.GetDouble(), 0, 1);
            });
    }

    /// <summary>Walks provider plan objects without relying on a particular nested-loop wrapper shape.</summary>
    private static IEnumerable<JsonElement> Objects(
        JsonElement value
    )
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            yield return value;

            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var property in value.EnumerateObject())
            {
                foreach (var nested in Objects(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var item in value.EnumerateArray())
            {
                foreach (var nested in Objects(item))
                {
                    yield return nested;
                }
            }
        }
    }
}
