namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionCapacityTests
{
    /// <summary>Qualifies independent facts against shared day-specific paths on the actual deep signal report.</summary>
    /// <param name="repeatedFacts">Whether several metrics and sources reuse each node/day path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task DeepSignalProofReuseRetainsResultsAndReportsNativeWork(bool repeatedFacts)
    {
        // Arrange
        const int nodes = 100_001;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, nodes, deep: true);
        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(setup, Engine);
        var data = await CompositionConsumerDataset.CreateAsync(database, Engine, nodes, deep: true);
        var request = repeatedFacts ? data.Report(16, 2, 3, 2, nearest: true)
            : data.Report(1, 1, 1, 1, nearest: false);

        // Act
        var observation = await CompositionConsumerCapacityProfiles.MeasureSignalsAsync(
            database, Engine, data, request, balanced: false, deep: true);

        // Assert
        CompositionConsumerCapacityProfiles.AssertAll([observation], _output);

        if (Engine == "MySql")
        {
            var work = CompositionPlanWork.ReadMySqlTree(observation.Plan);
            _output.WriteLine($"{observation.Profile}; maximumOperatorTuples={work.MaximumOperatorTuples}; "
                + $"maximumMaterializedTuples={work.MaximumMaterializedTuples}; "
                + $"rootOutputTuples={work.RootOutputTuples}; "
                + $"sumIteratorOutputTuples={work.SumIteratorOutputTuples}.");

            // WHY: MAX alone misses additional population passes below the largest iterator. The pinned
            // default plans emit about 16n/38n tuples across all stages; 18n/40n retain bounded headroom
            // while rejecting the four extra population passes observed with the JSON collection variant.
            // This sum counts intermediate outputs, not disjoint source rows or physical database reads.
            var maximumOutputSum = checked((repeatedFacts ? 40L : 18L) * nodes);

            Assert.InRange(work.SumIteratorOutputTuples, 0, maximumOutputSum);

            if (repeatedFacts)
            {
                // WHY: Nearest rejects one of this fixture's two measured nodes. At most sixteen anchors
                // and two days need one population pass each. Independent metrics and physical sources
                // must reuse that proof rather than multiply its allowance by their fact cardinality.
                var days = request.UntilExclusive.DayNumber - request.FromInclusive.DayNumber;
                var maximumOperatorTuples = checked((long)nodes * request.AnchorIds.Count * days);

                Assert.InRange(work.MaximumOperatorTuples, 0, maximumOperatorTuples);
            }
        }
    }
}
