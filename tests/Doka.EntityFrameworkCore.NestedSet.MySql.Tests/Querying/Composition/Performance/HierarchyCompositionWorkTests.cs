using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Checks executed MySQL work independently of machine-dependent elapsed time.</summary>
public sealed partial class HierarchyCompositionWorkTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    private const int Nodes = 100_000;
    private readonly ITestOutputHelper _output;

    /// <summary>Uses an isolated MySQL resource and retains native work observations.</summary>
    /// <param name="fixture">The provider-owned fixture; individual cases own their databases.</param>
    /// <param name="output">The sink retaining observed tuple and access-path work.</param>
    public HierarchyCompositionWorkTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _output = output;
    }

    /// <summary>
    /// Verifies the typed provider's pair semantics and required native-work contract.
    /// </summary>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task ObservePairProfilesAsync_ConfiguredProviderPairs_PreserveSemanticsAndQualifyAvailableNativeWork()
    {
        // Arrange
        await using var configured = new TreeContext(ModelCompatibilityDatabase.Options<TreeContext>(Engine));
        var family = configured.Database.IsMariaDb();
        var strategy = NestedSetProviderCapabilities
            .Resolve(configured)
            .ResolveSubtreeQueryStrategy(configured);

        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await PreparePairsAsync(database, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var observations = await ObservePairProfilesAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        // WHY: The installed typed adapter always exposes the configured family. The MySQL fixture
        // must qualify its required physical strategy instead of accepting a legacy semantic-only fallback.
        Assert.False(family);
        Assert.Equal(NestedSetSubtreeQueryStrategy.RelatedFirstUnion, strategy);
        Assert.All(observations, AssertObservation);
    }

    /// <summary>Distinguishes dense off-path failures, actual path rejection, and a valid full-length path.</summary>
    /// <param name="shape">The independent adjacency scenario; no database optimizer setting changes.</param>
    [Theory]
    [InlineData("DenseOffPath")]
    [InlineData("DenseOnPath")]
    [InlineData("CleanLongPath")]
    [Trait("Category", "Capacity")]
    public async Task ObserveAsync_DensePathEvidence_AvoidsRepeatedGlobalInvalidSetScans(
        string shape
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(
            setup,
            Nodes,
            deep: true,
            cancellationToken: TestContext.Current.CancellationToken,
            corrupt: false);

        await SeedPayloadAsync(setup, cancellationToken: TestContext.Current.CancellationToken);

        if (shape != "CleanLongPath")
        {
            await setup
                .Set<TreeNode>()
                .Where(node => node.NodeId <= Nodes / 2 && node.NodeId % 2 == 0)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.Payload, "blocked"),
                    CancellationToken.None);
        }

        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(
            setup,
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await QueryPlanTestSupport.RefreshStatisticsAsync<UnrelatedRow>(
            setup,
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var firstAnchor = shape == "DenseOffPath"
            ? Nodes - 999
            : 1;

        var expected = shape == "DenseOnPath"
            ? []
            : Expected(firstAnchor, 10, Nodes - 9, Nodes);

        var requiredEdges = shape == "DenseOnPath"
            ? 200L
            : Enumerable
                .Range(firstAnchor, 10)
                .Sum(anchor => Enumerable
                    .Range(Nodes - 9, 10)
                    .Sum(fact => (long)fact - anchor));

        var budget = CompositionPlanWorkBudget.ForPaths(Nodes, 100, requiredEdges);

        // Act
        var observation = await ObserveAsync(
            database,
            "path-" + shape,
            context => BuildPath(context, firstAnchor),
            expected,
            budget,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertObservation(observation);
    }

    /// <summary>Checks semantics, deferred scalar execution, and deterministic iterator work separately.</summary>
    private void AssertObservation(
        WorkObservation observation
    )
    {
        AssertSemanticsAndExecution(observation);
        Assert.InRange(observation.Work.MaximumOperatorTuples, 0, observation.Budget.OperatorTuples);
        Assert.InRange(observation.Work.MaximumMaterializedTuples, 0, observation.Budget.MaterializedTuples);
        Assert.InRange(observation.FullPrefixLoops * Nodes, 0, observation.Budget.OperatorTuples);
    }

    /// <summary>Checks the shared result and I/O contract while retaining all native work observations.</summary>
    private void AssertSemanticsAndExecution(
        WorkObservation observation
    )
    {
        _output.WriteLine(
            $"{observation.Profile}; "
            + $"maximumOperatorTuples={observation.Work.MaximumOperatorTuples}; "
            + $"maximumMaterializedTuples={observation.Work.MaximumMaterializedTuples}; "
            + $"fullTreePrefixLoops={observation.FullPrefixLoops}");

        Assert.Equal(observation.Expected, observation.Actual);
        Assert.Equal(0, observation.ConstructionCommands);
        Assert.Equal(1, observation.Commands);
        Assert.InRange(observation.Parameters, 0, 999);
        Assert.Equal(0, observation.Materialized);
        Assert.Equal(0, observation.Tracked);
        Assert.Equal(observation.Actual.Length, observation.Work.RootOutputTuples);
    }

    /// <summary>Keeps actual endpoint entities available to every public composition primitive.</summary>
    private sealed class Contribution
    {
        /// <summary>Gets the selected upper endpoint.</summary>
        public required TreeNode Anchor { get; init; }

        /// <summary>Gets the selected lower endpoint.</summary>
        public required TreeNode Node { get; init; }

        /// <summary>Gets the physically joined payload identity.</summary>
        public required int FactKey { get; init; }
    }

    /// <summary>Retains completed operation observations until the test's single assertion phase.</summary>
    private sealed record WorkObservation(
        string Profile,
        CompositionCapacityTotal[] Actual,
        CompositionCapacityTotal[] Expected,
        CompositionPlanWork Work,
        CompositionPlanWorkBudget Budget,
        double FullPrefixLoops,
        int ConstructionCommands,
        int Commands,
        int Parameters,
        int Materialized,
        int Tracked
    );
}
