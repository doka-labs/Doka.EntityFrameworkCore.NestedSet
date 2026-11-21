namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public sealed partial class HierarchyCompositionWorkTests
{
    /// <summary>Executes every existing profile regardless of whether optimized work can be qualified.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="database">The isolated database used by the scenario.</param>
    private async Task<List<WorkObservation>> ObservePairProfilesAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        var observations = new List<WorkObservation>();

        foreach (var anchors in new[] { 1, 1_000 })
        {
            foreach (var direct in new[] { false, true })
            {
                observations.Add(
                    await ObservePairsAsync(
                        database,
                        anchors,
                        100,
                        includeRoot: true,
                        direct,
                        cancellationToken: cancellationToken));
            }
        }

        observations.Add(
            await ObservePairsAsync(
                database,
                1_000,
                Nodes,
                includeRoot: true,
                direct: false,
                cancellationToken: cancellationToken));

        observations.Add(
            await ObservePairsAsync(
                database,
                1_000,
                Nodes,
                includeRoot: false,
                direct: false,
                cancellationToken: cancellationToken));

        return observations;
    }

    /// <summary>Observes a public subtree composition with a direct predicate or a physical payload join.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="database">The isolated database used by the scenario.</param>
    /// <param name="anchors">The selected hierarchy anchors.</param>
    /// <param name="facts">The synthetic consumer fact inputs.</param>
    /// <param name="includeRoot">Whether the selected hierarchy root participates.</param>
    /// <param name="direct">Whether the scenario bypasses consumer hierarchy composition.</param>
    private Task<WorkObservation> ObservePairsAsync(
        TestDatabase database,
        int anchors,
        int facts,
        bool includeRoot,
        bool direct,
        CancellationToken cancellationToken
    )
    {
        var firstAnchor = Nodes
            - anchors
            + (includeRoot
                ? 2
                : 1);

        var firstFact = Nodes - facts + 1;
        var expected = ExpectedPairs(firstAnchor, firstFact, includeRoot);
        var contributions = expected.Sum(row => row.Contributions);
        var budget = CompositionPlanWorkBudget.ForPairs(Nodes, anchors, facts, contributions);
        var profile = $"pairs-anchors-{anchors}-facts-{facts}-root-{includeRoot}-direct-{direct}";

        return ObserveAsync(
            database,
            profile,
            context => BuildPairs(context, firstAnchor, firstFact, includeRoot, direct),
            expected,
            budget,
            cancellationToken: cancellationToken);
    }

    /// <summary>Derives wide-tree totals from direct parent identity without consulting mapped boundaries.</summary>
    private static CompositionCapacityTotal[] ExpectedPairs(
        int firstAnchor,
        int firstFact,
        bool includeRoot
    )
    {
        var result = new List<CompositionCapacityTotal>();

        if (includeRoot)
        {
            var count = Nodes - firstFact + 1;
            result.Add(new CompositionCapacityTotal(1, count, ((long)firstFact + Nodes) * count / 2));
        }

        for (var key = Math.Max(firstAnchor, firstFact); key <= Nodes; key++)
        {
            result.Add(new CompositionCapacityTotal(key, 1, key));
        }

        return result.ToArray();
    }

    /// <summary>Derives the known chain's complete contribution bag without reading stored coordinates.</summary>
    private static CompositionCapacityTotal[] Expected(
        int firstAnchor,
        int anchors,
        int firstFact,
        int lastFact
    ) => Enumerable
        .Range(firstAnchor, anchors)
        .Select(anchor => new CompositionCapacityTotal(
            anchor,
            lastFact - firstFact + 1,
            ((long)firstFact + lastFact) * (lastFact - firstFact + 1) / 2))
        .ToArray();

    /// <summary>Retains the read-derived native plan before making deterministic work assertions.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="database">The isolated database used by the scenario.</param>
    /// <param name="profile">The selected synthetic workload profile.</param>
    /// <param name="build">The query factory used for construction and execution observations.</param>
    /// <param name="expected">The independently derived expected results.</param>
    /// <param name="budget">The unchanged native work budget.</param>
    private async Task<WorkObservation> ObserveAsync(
        TestDatabase database,
        string profile,
        Func<TreeContext, IQueryable<CompositionCapacityTotal>> build,
        CompositionCapacityTotal[] expected,
        CompositionPlanWorkBudget budget,
        CancellationToken cancellationToken
    )
    {
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        var commandProbe = new CompositionCommandProbe();
        await using var context = database.CreateContext(planProbe, commandProbe);
        var query = build(context);
        var constructionCommands = commandProbe.CommandCount;
        CompositionCapacityTotal[] actual;

        try
        {
            actual = await query.ToArrayAsync(cancellationToken);
        }
        catch (Exception error)
        {
            // WHY: Failure evidence must finish even when the failed read canceled its caller token.
            await QueryPlanTestSupport.WriteEvidenceAsync(
                "MySql-composition-work-failed-" + profile,
                [error.ToString(), query.ToQueryString()],
                cancellationToken: CancellationToken.None);

            throw;
        }

        var commands = commandProbe.CommandCount;
        var materialized = planProbe.MaterializedNodes;
        var tracked = context
            .ChangeTracker
            .Entries()
            .Count();

        var parameters = planProbe.ParameterBindings[0].Length;
        var plan = await QueryPlanTestSupport.ExplainAsync(
            context,
            planProbe,
            0,
            Engine,
            cancellationToken: cancellationToken);

        var work = CompositionPlanWork.ReadMySqlTree(plan);
        var fullPrefixLoops = CompositionPlanWork.FullTreePrefixLoops(plan);

        await QueryPlanTestSupport.WriteEvidenceAsync(
            "MySql-composition-work-" + profile,
            [
                $"Profile={profile}; maximumOperatorTuples={work.MaximumOperatorTuples}; "
                + $"maximumMaterializedTuples={work.MaximumMaterializedTuples}; "
                + $"fullTreePrefixLoops={fullPrefixLoops}; "
                + $"rootOutputTuples={work.RootOutputTuples}; actualResultRows={actual.Length}; "
                + $"operatorBudget={budget.OperatorTuples}; materializationBudget={budget.MaterializedTuples}",
                planProbe.Commands[0],
                plan,
            ],
            cancellationToken: cancellationToken);

        return new WorkObservation(
            profile,
            actual,
            expected,
            work,
            budget,
            fullPrefixLoops,
            constructionCommands,
            commands,
            parameters,
            materialized,
            tracked);
    }
}
