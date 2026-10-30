using System.Globalization;
using System.Text;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class DensePathWorkTestBase
{
    /// <summary>Separates construction, execution, command bytes and actual native work.</summary>
    private async Task<Observation> ObserveAsync(
        TestDatabase database,
        DensePathWorkScenario scenario,
        CompositionCapacityTotal[] expected,
        CompositionPlanWorkBudget budget
    )
    {
        var probe = new EnterpriseProbe(captureParameterBindings: true);
        var commands = new CompositionCommandProbe();
        await using var context = database.CreateContext(probe, commands);
        Func<object> build = () => Build(context, scenario);

        for (var warmup = 0; warmup < 8; warmup++)
        {
            GC.KeepAlive(build());
        }

        var allocation = QueryConstructionAllocation.Measure(build);
        var query = Build(context, scenario);
        var constructionCommands = commands.CommandCount;
        var profile = $"{scenario.Name}-nodes-{scenario.Nodes}-forest-{scenario.Forest}";
        CompositionCapacityTotal[] actual;

        try
        {
            actual = await query.ToArrayAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine + "-dense-path-work-failed-" + profile,
                [error.ToString(), probe.Commands.FirstOrDefault() ?? query.ToQueryString()]);

            throw;
        }

        var commandCount = commands.CommandCount;
        var materialized = probe.MaterializedNodes;
        var tracked = context
            .ChangeTracker
            .Entries()
            .Count();

        var parameters = probe.ParameterBindings[0].Length;
        var commandBytes = Encoding.UTF8.GetByteCount(probe.Commands[0]);
        var plan = await Specifications.QueryPlanTestSupport.ExplainAsync(context, probe, 0, Engine);
        var planBytes = Encoding.UTF8.GetByteCount(plan);

        // WHY: Keep the unchanged raw native plan even if its actual-counter reader rejects malformed evidence.
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-dense-path-work-native-" + profile,
            [
                $"Profile={profile}; visiblePopulation={scenario.VisibleNodes}; storedPopulation={scenario.StoredNodes}; "
                + $"construction={allocation} B/op; "
                + $"commandBytes={commandBytes}; planBytes={planBytes}; resultRows={actual.Length}",
                probe.Commands[0],
                plan,
            ]);

        var work = Engine == "MySql" ? DensePathNativeWork.MySql(plan) : DensePathNativeWork.MariaDb(plan);

        return new Observation(
            profile,
            actual,
            expected,
            work,
            budget,
            scenario.VisibleNodes,
            scenario.StoredNodes,
            constructionCommands,
            commandCount,
            parameters,
            materialized,
            tracked,
            allocation,
            commandBytes,
            planBytes);
    }

    /// <summary>Applies unchanged work/allocation budgets and the materialization population invariant.</summary>
    private void AssertObservation(
        Observation observation
    )
    {
        _output.WriteLine(
            $"{observation.Profile}; operatorTuples={observation.Work.OperatorTuples}; "
            + $"operatorMetric={(Engine == "MySql" ? "emitted iterator tuples" : "enumerated input tuples")}; "
            + $"postIndexInputTuples={observation.Work.PostIndexTuples?.ToString(CultureInfo.InvariantCulture)
                ?? "not exposed"}; "
            + $"materializedOutput={observation.Work.MaterializedOutput?.ToString(CultureInfo.InvariantCulture)
                ?? "not exposed"}; "
            + $"unknownMaterializedOutputs={observation.Work.UnknownMaterializedOutputs}; "
            + $"fullPrefixLoops={observation.Work.FullPrefixLoops?.ToString(CultureInfo.InvariantCulture)
                ?? "not exposed"}; "
            + $"visiblePopulation={observation.VisiblePopulation}; storedPopulation={observation.StoredPopulation}; "
            + $"construction={observation.Allocation} B/op; commandBytes={observation.CommandBytes}; "
            + $"planBytes={observation.PlanBytes}; operatorBudget={observation.Budget.OperatorTuples}; "
            + $"materializedOutputBudget={observation.Budget.MaterializedTuples}");

        Assert.Equal(observation.Expected, observation.Actual);
        Assert.Equal(0, observation.ConstructionCommands);
        Assert.Equal(1, observation.Commands);
        Assert.InRange(observation.Parameters, 0, 999);
        Assert.Equal(0, observation.Materialized);
        Assert.Equal(0, observation.Tracked);
        Assert.InRange(observation.Allocation, 1, 256 * 1_024);
        Assert.True(observation.CommandBytes > 0);
        Assert.True(observation.PlanBytes > 0);
        Assert.True(observation.Work.ExecutedOperators > 0);

        if (observation.Work.RootRows is { } rows)
        {
            Assert.Equal((double)observation.Actual.Length, rows);
        }

        Assert.InRange(observation.Work.OperatorTuples, 0, observation.Budget.OperatorTuples);

        if (observation.Work.MaterializedOutput is { } output)
        {
            // WHY: A one-time input scan may read physical decoys, but each stored failure relation can emit
            // only nodes from the visible authorized universe. Unexposed output is reported as unknown.
            Assert.InRange(output, 0, observation.Budget.MaterializedTuples);
            Assert.InRange(output, 0, observation.VisiblePopulation);
        }

        if (observation.Work.FullPrefixLoops is { } loops)
        {
            Assert.InRange(loops * observation.VisiblePopulation, 0, observation.Budget.OperatorTuples);
        }
    }

    /// <summary>Retains one completed operation until the test's single assertion phase.</summary>
    private sealed record Observation(
        string Profile,
        CompositionCapacityTotal[] Actual,
        CompositionCapacityTotal[] Expected,
        DensePathNativeWork Work,
        CompositionPlanWorkBudget Budget,
        int VisiblePopulation,
        int StoredPopulation,
        int ConstructionCommands,
        int Commands,
        int Parameters,
        int Materialized,
        int Tracked,
        long Allocation,
        int CommandBytes,
        int PlanBytes
    );
}
