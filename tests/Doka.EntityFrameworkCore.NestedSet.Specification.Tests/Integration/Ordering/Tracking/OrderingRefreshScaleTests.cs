namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Measures native-key refresh scale and actual provider query shapes using the existing ordering model.
/// </summary>
public abstract partial class OrderingRefreshScaleTests : ProviderTest
{
    private readonly OrderingFixture _fixture;
    private readonly RelationalFixture _relationalFixture;
    private readonly OrderingKeyFixture _keyFixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Uses isolated fixture databases and persists measurements in the test result.</summary>
    protected OrderingRefreshScaleTests(
        IProviderFixture<OrderingFixture> fixture,
        IProviderFixture<RelationalFixture> relationalFixture,
        IProviderFixture<OrderingKeyFixture> keyFixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _relationalFixture = relationalFixture.Value;
        _keyFixture = keyFixture.Value;
        _output = output;
    }

    /// <summary>
    ///     Bounds refresh commands and rows for a large shared scope and a sparsely tracked larger forest.
    /// </summary>
    [Theory]
    [InlineData(5000, false, false)]
    [InlineData(100000, true, false)]
    [InlineData(5000, false, true)]
    public async Task NativeRefreshReadsOnlyTrackedRowsWithBoundedCommands(
        int nodes,
        bool sparse,
        bool adjacent
    )
    {
        // Arrange
        await OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, nodes);
        using var probe = new ScaleProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var query = context
            .Set<OrderingNode>()
            .Where(node => node.ParentId != null)
            .OrderBy(node => node.Id)
            .AsQueryable();

        if (sparse)
        {
            query = query.Where(node => node.Id == 1);
        }

        var tracked = await query.ToArrayAsync(CancellationToken.None);
        tracked[0].Name = adjacent ? "node-00000002z" : "zzzz";
        probe.Observe(context);
        probe.Reset();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();

        // Act
        var saved = await context.SaveChangesAsync(false, CancellationToken.None);
        var elapsed = timer.Elapsed;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        // Assert
        var lastWrite = probe.Commands.FindLastIndex(command => command
            .Sql
            .TrimStart()
            .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase));

        var refresh = probe
            .Commands
            .Skip(lastWrite + 1)
            .ToArray();
        var affectedTracked = sparse
            ? 1
            : adjacent
                ? 2
                : nodes;

        var finalPosition = adjacent ? 1 : nodes - 1;
        _output.WriteLine(
            $"Engine={Engine}; forest={nodes}; tracked={tracked.Length}; adjacent={adjacent}; "
            + $"refresh commands={refresh.Length}; "
            + $"read calls={refresh.Sum(command => command.ReadCalls)}; compilations={probe.Compilations}; "
            + $"allocated process bytes={allocated}; elapsed ms={elapsed.TotalMilliseconds:F2}; "
            + $"maximum parameters={refresh.Max(command => command.Parameters)}");

        Assert.Equal(1, saved);
        Assert.True(lastWrite >= 0);
        Assert.Equal(
            ((finalPosition * 2) + 2, (finalPosition * 2) + 3, finalPosition),
            (tracked[0].Left, tracked[0].Right, tracked[0].Position));
        Assert.True(
            context
                .Entry(tracked[0])
                .Property(node => node.Name)
                .IsModified);
        Assert.InRange(refresh.Length, 1, 2);
        Assert.InRange(refresh.Sum(command => command.ReadCalls), affectedTracked, affectedTracked + 2);
        Assert.All(
            refresh,
            command =>
            {
                Assert.InRange(command.Parameters, 1, 16);
                Assert.DoesNotContain("Payload", command.Sql, StringComparison.Ordinal);
                Assert.DoesNotContain("Name", command.Sql, StringComparison.Ordinal);
            });

        Assert.All(
            tracked.Skip(1),
            node =>
            {
                var expectedPosition = adjacent && node.Id > 2 ? node.Id - 1 : node.Id - 2;
                Assert.Equal((expectedPosition * 2) + 2, node.Left);
            });
    }

    /// <summary>
    ///     Executes changing key cardinalities through one collection parameter and observes real cache misses.
    /// </summary>
    [Fact]
    public async Task NativeTrackedKeyCollectionKeepsOneShapeBeyondProviderScalarLimit()
    {
        // Arrange
        await OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, 5000);
        await using var services = CacheTestServices.Create(Engine);
        await using var template = await _fixture.CreateContextAsync(Engine);
        using var probe = new ScaleProbe();
        await using var context = OrderingRefreshTestSupport.CreateCacheContext(template, services, probe);
        var cardinalities = new[] { 1, 63, 64, 65, 5000 };
        var tag = "Collection cardinality measurement " + Guid.NewGuid().ToString("N");
        var map = NestedSetMapping<OrderingNode, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(OrderingNode))!);

        var capabilities = NestedSetProviderCapabilities.Resolve(context);
        var coldCompilations = 0;
        var results = new List<int>();
        probe.Observe(context);
        probe.Reset();

        // Act
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var count in cardinalities)
            {
                var keys = Enumerable
                    .Range(1, count)
                    .ToArray();

                var rows = await ReadParameterizedAsync(context, keys, tag);
                results.Add(rows.Length);
            }

            if (pass == 0)
            {
                coldCompilations = probe.Compilations;
            }
        }

        // Assert
        _output.WriteLine(
            $"Engine={Engine}; cardinalities={string.Join(',', cardinalities)}; "
            + $"compilations={probe.Compilations}; parameter counts="
            + string.Join(',', probe.Commands.Select(command => command.Parameters)));

        _output.WriteLine(probe.Commands[^1].Sql);

        Assert.True(map.HasNativeKeyEquality);
        Assert.True(capabilities.SupportsTrackedKeyCollection(map.KeyProperty));
        Assert.Equal(cardinalities.Concat(cardinalities), results);
        Assert.Equal(1, coldCompilations);
        Assert.Equal(coldCompilations, probe.Compilations);
        Assert.Equal(cardinalities.Length * 2, probe.Commands.Count);
        Assert.All(probe.Commands, command => Assert.Equal(1, command.Parameters));

        if (Engine is "MySql" or "MariaDb")
        {
            Assert.All(
                probe.Commands,
                command =>
                {
                    Assert.Contains("CROSS JOIN JSON_TABLE", command.Sql, StringComparison.Ordinal);
                    Assert.DoesNotContain("JSON_CONTAINS", command.Sql, StringComparison.Ordinal);
                });
        }
    }

    /// <summary>Records actual ordinal fallback cache behavior before considering any padding policy.</summary>
    [Fact]
    public async Task OrdinalFallbackReportsActualCompilationCardinalities()
    {
        // Arrange
        await OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, 64);
        await using var services = CacheTestServices.Create(Engine);
        await using var template = await _fixture.CreateContextAsync(Engine);
        using var probe = new ScaleProbe();
        await using var context = OrderingRefreshTestSupport.CreateCacheContext(template, services, probe);
        var cardinalities = new[] { 1, 2, 63, 64 };
        var tag = "Ordinal cardinality measurement " + Guid.NewGuid().ToString("N");
        var coldCompilations = 0;
        var results = new List<int>();
        var correlations = new List<(int[] ExpectedKeys, int[] Ordinals, int[] ActualKeys)>();
        probe.Observe(context);
        probe.Reset();

        // Act
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var count in cardinalities)
            {
                var keys = Enumerable
                    .Range(1, count)
                    .ToArray();

                if (pass == 1)
                {
                    Array.Reverse(keys);
                }

                var rows = await NestedSetTrackedRowset<OrderingNode>
                    .Match(
                        context
                            .Set<OrderingNode>()
                            .AsNoTracking()
                            .TagWith(tag),
                        nameof(OrderingNode.Id),
                        keys)
                    .Select(row => new
                    {
                        row.Ordinal,
                        row.Entity.Id
                    })
                    .ToArrayAsync(CancellationToken.None);

                results.Add(rows.Length);
                var ordered = rows
                    .OrderBy(row => row.Ordinal)
                    .ToArray();

                correlations.Add(
                    (keys, ordered.Select(row => row.Ordinal).ToArray(),
                        ordered.Select(row => row.Id).ToArray()));
            }

            if (pass == 0)
            {
                coldCompilations = probe.Compilations;
            }
        }

        // Assert
        _output.WriteLine(
            $"Engine={Engine}; ordinal cardinalities={string.Join(',', cardinalities)}; "
            + $"actual compilations={probe.Compilations}; SQL characters="
            + string.Join(',', probe.Commands.Select(command => command.Sql.Length)));

        Assert.Equal(cardinalities.Concat(cardinalities), results);
        Assert.Equal(4, coldCompilations);
        Assert.Equal(coldCompilations, probe.Compilations);
        Assert.All(
            correlations,
            values =>
            {
                Assert.Equal(Enumerable.Range(0, values.ExpectedKeys.Length), values.Ordinals);
                Assert.Equal(values.ExpectedKeys, values.ActualKeys);
            });
    }

    /// <summary>
    ///     Uses the same LINQ expression for every cardinality so the diagnostic measures cache behavior.
    /// </summary>
    private static Task<int[]> ReadParameterizedAsync(
        OrderingContext context,
        int[] keys,
        string tag
    )
    {
        var map = NestedSetMapping<OrderingNode, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(OrderingNode))!);

        var capabilities = NestedSetProviderCapabilities.Resolve(context);

        return capabilities
            .MatchTrackedKeyCollection(
                context
                    .Set<OrderingNode>()
                    .AsNoTracking()
                    .TagWith(tag),
                map.KeyProperty,
                keys)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);
    }
}
