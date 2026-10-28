namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Guards actual broad-payload tuple work independently of database duration or inactive plan costs.</summary>
[Collection("Allocation measurements")]
public sealed class NearestDecisionWorkTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, PostgreSqlEngine>>
{
    private readonly ITestOutputHelper _output;

    /// <summary>Uses an isolated PostgreSQL fixture and retains native work observations.</summary>
    /// <param name="fixture">The provider resource owning the case's database.</param>
    /// <param name="output">The sink retaining actual work and the unchanged pair budgets.</param>
    public NearestDecisionWorkTests(
        ProviderFixture<RelationalFixture, PostgreSqlEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _output = output;
    }

    /// <summary>One broad statement may scan its population once but cannot replay it per hierarchy node.</summary>
    /// <param name="nodes">The persisted population shared by the measured broad profiles.</param>
    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [Trait("Category", "Capacity")]
    public async Task BroadPayloadDoesNotRepeatWholePopulationMaterialization(int nodes)
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, nodes);
        int[] anchors = [1, 10, 100, 1_000];

        // Act
        // WHY: Reuse the exact composed capacity query and native replay. A hand-written nearest
        // query would miss the inactive subplans that distorted the full payload join's estimate.
        var observations = await CompositionCapacityProfiles.MeasureAsync(database, Engine, nodes, broad: true);

        // Assert
        Assert.Equal(anchors.Length, observations.Count);
        CompositionCapacityProfiles.AssertAll(observations, _output);

        foreach (var (observation, count) in observations.Zip(anchors))
        {
            var contributions = observation.Expected.Sum(row => row.Contributions);
            var budget = CompositionPlanWorkBudget.ForPairs(nodes, count, nodes, contributions);
            var work = CompositionPlanWork.ReadPostgreSqlJson(observation.Plan);

            _output.WriteLine($"{observation.Profile}; emitted={work.MaximumOperatorTuples}; "
                + $"rejected={work.MaximumRejectedJoinTuples}; maximum={budget.OperatorTuples}");
            Assert.Equal(observation.Expected.Length, work.RootOutputTuples);
            Assert.InRange(work.MaximumOperatorTuples, 0, budget.OperatorTuples);
            Assert.InRange(work.MaximumRejectedJoinTuples, 0, budget.OperatorTuples);
        }
    }
}
