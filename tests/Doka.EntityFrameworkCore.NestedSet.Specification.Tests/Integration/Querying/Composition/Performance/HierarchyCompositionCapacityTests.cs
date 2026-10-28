namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Measures scalar hierarchy composition on persisted medium populations without a CPU release gate.</summary>
public abstract partial class HierarchyCompositionCapacityTests : ProviderTest
{
    private readonly ITestOutputHelper _output;

    /// <summary>Uses the provider's immutable engine identity and output sink.</summary>
    /// <param name="fixture">The provider-owned database resource.</param>
    /// <param name="output">The sink retaining profile measurements.</param>
    protected HierarchyCompositionCapacityTests(
        IProviderFixture<RelationalFixture> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _output = output;
    }

    /// <summary>Qualifies each anchor count with selective and broad payload on two branching shapes.</summary>
    /// <param name="nodes">The medium persisted population, seeded once for all associated read profiles.</param>
    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [Trait("Category", "Capacity")]
    public async Task MediumCompositionKeepsCommandsMaterializationAndConstructionBounded(
        int nodes
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, nodes);
        var observations = new List<CompositionCapacityObservation>();
        var consumers = new List<CompositionConsumerCapacityObservation>();

        // Act
        observations.AddRange(await CompositionCapacityProfiles.MeasureAsync(database, Engine, nodes));
        observations.AddRange(await CompositionCapacityProfiles.MeasureAsync(database, Engine, nodes, broad: true));
        consumers.AddRange(await CompositionConsumerCapacityProfiles.MeasureAsync(database, Engine, nodes));
        await CompositionCapacityProfiles.BalanceAsync(setup, nodes);
        observations.AddRange(await CompositionCapacityProfiles.MeasureAsync(database, Engine, nodes, balanced: true));
        observations.AddRange(await CompositionCapacityProfiles.MeasureAsync(
            database, Engine, nodes, balanced: true, broad: true));
        consumers.AddRange(await CompositionConsumerCapacityProfiles.MeasureAsync(
            database, Engine, nodes, balanced: true));

        var validation = await setup.NestedSet<TreeNode>().ForScope(1).InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.True(validation.IsValid, string.Join(", ", validation.Issues.Select(issue => issue.Code)));
        Assert.Equal(nodes, validation.NodeCount);
        Assert.Equal(24, observations.Count);
        CompositionCapacityProfiles.AssertAll(observations, _output);
        CompositionConsumerCapacityProfiles.AssertAll(consumers, _output);
    }
}
