namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies actual consumer queries independently of unrelated generic query strategies.</summary>
public abstract partial class HierarchyCompositionCapacityTests
{
    /// <summary>Checks actual reports, person selection and bounded chains on a persisted large hierarchy.</summary>
    /// <param name="deep">Whether to use the depth-100000 chain instead of wide and balanced branches.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task LargeConsumerCompositionRetainsItsResultsAndAllocationBudgets(bool deep)
    {
        // Arrange
        const int nodes = 100_001;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var context = database.CreateContext();
        await CapacitySeed.SeedAsync(context, nodes, deep: deep);
        // WHY: The shared generic profiles prepare these physical statistics before their consumer
        // reads. This independent entry point must do the same; fresh-row estimates otherwise hide
        // the actual hundred-thousand-node population behind a one-row view estimate.
        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(context, Engine);
        var observations = new List<CompositionConsumerCapacityObservation>();

        // Act
        // WHY: Consumer evidence uses the actual sample builders. An unrelated generic access-path
        // failure must remain visible in its own test without preventing these operations from running.
        observations.AddRange(await CompositionConsumerCapacityProfiles.MeasureAsync(
            database, Engine, nodes, deep: deep));

        if (!deep)
        {
            await CompositionCapacityProfiles.BalanceAsync(context, nodes);
            await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(context, Engine);
            observations.AddRange(await CompositionConsumerCapacityProfiles.MeasureAsync(
                database, Engine, nodes, balanced: true));
        }

        var validation = await context.NestedSet<TreeNode>().ForScope(1).InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.True(validation.IsValid, string.Join(", ", validation.Issues.Select(issue => issue.Code)));
        Assert.Equal(nodes, validation.NodeCount);
        CompositionConsumerCapacityProfiles.AssertAll(observations, _output);

        if (Engine == "MySql" && !deep)
        {
            // WHY: Template reuse must not multiply the wide or balanced report's iterator work.
            // These fixed consumers permit a bounded number of linear population passes, while both
            // the largest iterator and their sum reject repeated payload scans hidden by correct results.
            var maximumOperatorTuples = checked(4L * nodes);
            var maximumOutputSum = checked(8L * nodes);
            var signals = observations.Where(observation =>
                observation.Profile.StartsWith("signal-", StringComparison.Ordinal)).ToArray();

            Assert.NotEmpty(signals);

            foreach (var observation in signals)
            {
                var work = CompositionPlanWork.ReadMySqlTree(observation.Plan);
                _output.WriteLine($"{observation.Profile}; maximumOperatorTuples={work.MaximumOperatorTuples}; "
                    + $"sumIteratorOutputTuples={work.SumIteratorOutputTuples}.");

                Assert.InRange(work.MaximumOperatorTuples, 0, maximumOperatorTuples);
                Assert.InRange(work.SumIteratorOutputTuples, 0, maximumOutputSum);
            }
        }
    }
}
