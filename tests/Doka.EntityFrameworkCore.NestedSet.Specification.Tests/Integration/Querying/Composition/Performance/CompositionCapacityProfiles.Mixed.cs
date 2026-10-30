namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionCapacityProfiles
{
    /// <summary>Measures one-reader upward/downward composition with a selective scalar result per source.</summary>
    private static async Task<IReadOnlyList<CompositionCapacityObservation>> MeasureMixedAsync(
        TestDatabase database,
        string engine,
        int nodes,
        bool balanced
    )
    {
        var observations = new List<CompositionCapacityObservation>();

        foreach (var origins in new[] { 1, 10, 100, 1_000 })
        {
            var planProbe = new EnterpriseProbe(captureParameterBindings: true);
            var commandProbe = new CompositionCommandProbe();
            await using var context = database.CreateContext(planProbe, commandProbe);
            var firstOrigin = nodes - origins + 1;
            var buildBytes = MeasureConstruction(() => BuildMixed(context, firstOrigin, nodes));
            var query = BuildMixed(context, firstOrigin, nodes);
            var constructionCommands = commandProbe.CommandCount;
            var profile = $"mixed-{(balanced ? "balanced" : "wide")}-{nodes}-origins-{origins}-target-1";
            var started = Stopwatch.GetTimestamp();
            CompositionCapacityTotal[] rows;

            try
            {
                rows = await query.ToArrayAsync(CancellationToken.None);
            }
            catch (Exception error)
            {
                await RetainFailureAsync(context, engine, profile, planProbe, error);

                throw;
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            var commands = commandProbe.CommandCount;
            var materialized = planProbe.MaterializedNodes;
            var tracked = context.ChangeTracker.Entries().Count();
            string plan;

            try
            {
                plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, 0, engine);
            }
            catch (Exception error)
            {
                await RetainFailureAsync(context, engine, profile, planProbe, error);

                throw;
            }

            var expected = Enumerable.Range(firstOrigin, origins)
                .Where(origin => !balanced || SameBranch(origin, nodes))
                .Select(origin => new CompositionCapacityTotal(origin, 1, nodes))
                .ToArray();

            var observation = new CompositionCapacityObservation(profile, rows, expected, commands,
                planProbe.ParameterBindings[0].Length,
                constructionCommands, materialized, tracked, buildBytes, elapsed, plan);

            observations.Add(observation);
            await WriteAsync(engine, observation, planProbe.Commands[0]);

            if (engine == "SqlServer")
            {
                // WHY: Independent adjacency gives one upward edge and one downward edge, or two
                // downward edges for a balanced branch origin. One population pass is allowed;
                // reading the entire owner population again for every origin is not requested work.
                var requiredEdges = expected.Sum(row => balanced && (row.Anchor - 2) % BalancedBranchSize == 0
                    ? 3L : 2L);

                CompositionSqlServerPlanWork.AssertWithinBudget(plan,
                    CompositionPlanWorkBudget.ForPaths(nodes, origins, requiredEdges), profile);
            }
        }

        return observations;
    }

    /// <summary>Preserves the source and pivot while expanding only the requested leaf under that pivot.</summary>
    private static IQueryable<CompositionCapacityTotal> BuildMixed(
        TreeContext context,
        int firstOrigin,
        int target
    )
    {
        var hierarchy = context.NestedSet<TreeNode>().ForScope(1).InTree(Guid.Empty);
        var up = hierarchy.SelectAncestors(source => source.NodeId >= firstOrigin,
                (source, pivot, distance) => new { Source = source, Pivot = pivot, UpDistance = distance })
            .Where(row => row.UpDistance == 1);

        var down = hierarchy.SelectSubtrees(pivot => up.Any(row => row.Pivot == pivot),
                (pivot, node, distance) => new { Pivot = pivot, Target = node, DownDistance = distance })
            .Where(row => row.Target.NodeId == target);

        var mixed = up.Join(down, row => row.Pivot, row => row.Pivot,
            (first, second) => new { first.Source, first.Pivot, second.Target });

        var allowedUp = hierarchy.WherePathEdges(mixed,
            row => row.Pivot, row => row.Source, (_, parent, child) => child.Payload != "blocked");

        var allowedBoth = hierarchy.WherePathEdges(allowedUp,
            row => row.Pivot, row => row.Target, (_, parent, child) => child.Payload != "blocked");

        return allowedBoth.GroupBy(row => row.Source.NodeId)
            .OrderBy(group => group.Key)
            .Select(group => new CompositionCapacityTotal(
                group.Key, group.LongCount(), group.Sum(row => (long)row.Target.NodeId)));
    }

    /// <summary>Uses independent adjacency to find a source whose direct parent contains the target.</summary>
    private static bool SameBranch(
        int origin,
        int target
    ) => (origin - 2) % BalancedBranchSize == 0
        || (origin - 2) / BalancedBranchSize == (target - 2) / BalancedBranchSize;
}
