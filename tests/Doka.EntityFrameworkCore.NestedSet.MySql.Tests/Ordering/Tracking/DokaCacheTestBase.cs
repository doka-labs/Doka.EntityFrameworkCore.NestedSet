namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Measures the Doka-family refresh cache with independently reseeded ordering forests.</summary>
public abstract class DokaCacheTestBase : ProviderTest
{
    private readonly OrderingFixture _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Uses the ordering database and test output needed by this cache measurement.</summary>
    /// <param name="fixture">The engine-bound ordering fixture.</param>
    /// <param name="output">The measurement's test-output sink.</param>
    protected DokaCacheTestBase(
        IProviderFixture<OrderingFixture> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _output = output;
    }

    /// <summary>Measures independently reseeded Doka saves after warming the identical complete query shape.</summary>
    /// <param name="adjacent">Whether the rename shifts only adjacent siblings rather than the full forest.</param>
    /// <param name="sample">The independent reseed and measurement sample number.</param>
    /// <returns>A task completing after cache-shape, position, compilation, and refresh assertions.</returns>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task WarmDokaSaveMeasuresIndependentReseededForest(
        bool adjacent,
        int sample
    )
    {
        // Arrange
        await OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, 5000);
        await using var services = CacheTestServices.Create(Engine);
        await using var template = await _fixture.CreateContextAsync(Engine);

        await using (var warm = OrderingRefreshTestSupport.CreateCacheContext(template, services))
        {
            var warmTracked = await warm
                .Set<OrderingNode>()
                .Where(node => node.ParentId != null)
                .OrderBy(node => node.Id)
                .ToArrayAsync(CancellationToken.None);

            warmTracked[0].Name = adjacent ? "node-00000002z" : "zzzz";
            await warm.SaveChangesAsync(false, CancellationToken.None);
        }

        // WHY: Warmup must not leave a no-op rename or changed tree for measurement; reset every persisted row.
        await OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, 5000);
        using var probe = new ScaleProbe();
        await using var context = OrderingRefreshTestSupport.CreateCacheContext(template, services, probe);
        var tracked = await context
            .Set<OrderingNode>()
            .Where(node => node.ParentId != null)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

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

        var expectedPosition = adjacent ? 1 : 4999;
        _output.WriteLine(
            $"Engine={Engine}; sample={sample}; tracked=5000; adjacent={adjacent}; "
            + $"refresh commands={refresh.Length}; read calls={refresh.Sum(command => command.ReadCalls)}; "
            + $"compilations={probe.Compilations}; allocated process bytes={allocated}; "
            + $"elapsed ms={elapsed.TotalMilliseconds:F2}");

        Assert.Equal(1, saved);
        Assert.Equal(
            ((expectedPosition * 2) + 2, (expectedPosition * 2) + 3, expectedPosition),
            (tracked[0].Left, tracked[0].Right, tracked[0].Position));
        Assert.Equal(0, probe.Compilations);
        Assert.True(lastWrite >= 0);
        Assert.NotEmpty(refresh);
        Assert.All(
            tracked.Skip(1),
            node =>
            {
                var position = adjacent && node.Id > 2 ? node.Id - 1 : node.Id - 2;
                Assert.Equal(
                    ((position * 2) + 2, (position * 2) + 3, position),
                    (node.Left, node.Right, node.Position));
            });
    }
}
