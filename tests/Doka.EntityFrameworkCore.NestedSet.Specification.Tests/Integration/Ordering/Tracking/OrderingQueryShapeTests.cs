using System.Security.Cryptography;
using System.Text;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Measures executed production refresh shapes across disjoint intervals and ordinal key batches.</summary>
public abstract partial class OrderingQueryShapeTests : ProviderTest
{
    // WHY: This fixed 64-key/64-region fixture must not emit hundreds of kilobytes of repeated interval predicates.
    private const int MaximumRefreshSqlCharacters = 100_000;

    private readonly OrderingKeyFixture _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Reuses the real string-key provider model and writes observations into each test result.</summary>
    protected OrderingQueryShapeTests(
        IProviderFixture<OrderingKeyFixture> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _output = output;
    }

    /// <summary>Gets the sampled product of changed intervals and second key-batch lengths.</summary>
    public static TheoryData<int, int> Cases
    {
        get
        {
            var cases = new TheoryData<int, int>();
            int[] cardinalities = [1, 2, 63, 64];

            foreach (var intervals in cardinalities)
            {
                foreach (var tail in cardinalities)
                {
                    cases.Add(intervals, tail);
                }
            }

            return cases;
        }
    }

    /// <summary>Reuses warmed production shapes with different scope values and native database key aliases.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ProductionOrdinalRefreshReusesItsWarmedShapes(
        int intervals,
        int tail
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        await using var services = CacheTestServices.Create(Engine);
        using var warmProbe = new ScaleProbe();
        var warmOrdinals = new OrdinalProbe();
        await using var warm = CreateContext(setup, warmProbe, warmOrdinals, services);
        var warmTracked = AttachScenario(warm, 1, intervals, tail);
        warmProbe.Observe(warm);
        var tag = "Production shape observer control " + Guid.NewGuid().ToString("N");
        await warm
            .Set<OrderingKeyNode<string, string?>>()
            .AsNoTracking()
            .TagWith(tag)
            .Select(node => node.Id)
            .Take(1)
            .ToArrayAsync(CancellationToken.None);

        var controlCompilations = warmProbe.Compilations;
        warmProbe.Reset();
        warmOrdinals.Reset();
        await warm.SaveChangesAsync(false, CancellationToken.None);
        var warmed = Capture(warmProbe, warmOrdinals);
        var unchangedScope = await ReadScopeAsync(setup, 1);
        using var measuredProbe = new ScaleProbe();
        var measuredOrdinals = new OrdinalProbe();
        await using var measured = CreateContext(setup, measuredProbe, measuredOrdinals, services);
        var tracked = AttachScenario(measured, 2, intervals, tail);
        measuredProbe.Observe(measured);

        // Act
        var saved = await measured.SaveChangesAsync(false, CancellationToken.None);

        // Assert
        var observation = Capture(measuredProbe, measuredOrdinals);
        WriteObservation(Engine, intervals, tail, "warmup", warmed);
        WriteObservation(Engine, intervals, tail, "measured", observation);
        _output.WriteLine($"Separate tagged observer cold control compilations={controlCompilations}");

        if (intervals == 64
            && tail == 64)
        {
            var sqlEvidence = observation
                .Commands
                .Select((command, index) =>
                    $"Refresh command={index}; SQL characters={command.Sql.Length}; parameters={command.Parameters}\n"
                    + command.Sql)
                .ToArray();

            await QueryPlanTestSupport.WriteEvidenceAsync($"production-refresh-{Engine}-R64-K64", sqlEvidence);
        }

        Assert.Equal(1, controlCompilations);
        Assert.Equal(intervals, saved);
        Assert.Equal(0, warmProbe.ActiveReaders);
        Assert.Equal(0, measuredProbe.ActiveReaders);
        Assert.Equal(0, observation.RefreshCompilations);
        Assert.True(warmed.LastStructuralWrite >= 0);
        Assert.True(observation.LastStructuralWrite >= 0);
        Assert.Equal(3, warmed.Commands.Length);
        Assert.Equal(3, observation.Commands.Length);
        Assert.Equal(
            warmed.Commands.Select(command => command.Sql),
            observation.Commands.Select(command => command.Sql));

        AssertOrdinals(warmed, intervals, tail);
        AssertOrdinals(observation, intervals, tail);
        AssertTracked(warm, warmTracked, intervals, tail);
        AssertTracked(measured, tracked, intervals, tail);
        var persisted = await ReadScopeAsync(setup, 2);
        AssertPersisted(persisted, intervals);
        await AssertRootAsync(setup, 1);
        await AssertRootAsync(setup, 2);
        Assert.Equal(intervals, CountChangedIntervals(persisted));
        Assert.Equal(unchangedScope.Select(Coordinates), (await ReadScopeAsync(setup, 1)).Select(Coordinates));
        Assert.All(
            observation.Commands,
            command =>
            {
                Assert.Contains(nameof(OrderingKeyNode<,>.Scope), command.Sql, StringComparison.Ordinal);
                Assert.DoesNotContain(nameof(OrderingKeyNode<,>.Name), command.Sql, StringComparison.Ordinal);
                Assert.InRange(command.Sql.Length, 1, MaximumRefreshSqlCharacters);
            });
    }

    /// <summary>Records actual command fingerprints without interpreting hypothetical cache residency.</summary>
    private void WriteObservation(
        string engine,
        int intervals,
        int tail,
        string phase,
        ShapeObservation observed
    )
    {
        _output.WriteLine(
            $"Engine={engine}; R={intervals}; K={tail}; tracked={64 + tail}; phase={phase}; "
            + $"save compilations={observed.SaveCompilations}; refresh compilations={observed.RefreshCompilations}");

        _output.WriteLine("Last actual structural command:");
        _output.WriteLine(observed.LastStructuralSql);

        for (var index = 0; index < observed.Commands.Length; index++)
        {
            var command = observed.Commands[index];
            var bytes = Encoding.UTF8.GetBytes(command.Sql);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var ordinals = index < observed.Readers.Length
                ? string.Join(',', observed.Readers[index].Ordinals)
                : "missing reader";

            _output.WriteLine(
                $"Refresh command={index}; SQL characters={command.Sql.Length}; "
                + $"parameters={command.Parameters}; reader calls={command.ReadCalls}; SHA256={hash}; "
                + $"ordinals={ordinals}");
        }
    }
}
