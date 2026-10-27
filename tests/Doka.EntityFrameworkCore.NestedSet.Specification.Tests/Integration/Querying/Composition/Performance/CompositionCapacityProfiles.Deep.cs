namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionCapacityProfiles
{
    /// <summary>Reuses the existing hundred-thousand-depth fixture for selective and overlapping deep reads.</summary>
    /// <param name="database">The capacity case's valid persisted chain.</param>
    /// <param name="engine">The provider whose executed plans are retained.</param>
    /// <returns>The bounded grouped results and operation-only observations.</returns>
    internal static async Task<IReadOnlyList<CompositionCapacityObservation>> MeasureDeepAsync(
        TestDatabase database,
        string engine
    )
    {
        var observations = new List<CompositionCapacityObservation>();
        await using var setup = database.CreateContext();
        await SeedPayloadAsync(setup, 100_001, 100_001);
        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(setup, engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<UnrelatedRow>(setup, engine);

        foreach (var depth in new[] { 1_000, 10_000, 100_000 })
        {
            foreach (var facts in new[] { 1, 10, 100 })
            {
                observations.Add(await MeasureDeepProfileAsync(database, engine, depth, facts, 1, "Path"));
            }

            // WHY: Dense acceptance and rejection are separate from full root-owner paths; a selective failing
            // value must not conceal repeated candidate scans or expensive successful completeness checks.
            foreach (var shape in new[] { "NearestAccept", "NearestReject", "RootOwner" })
            {
                observations.Add(await MeasureDeepProfileAsync(database, engine, depth, 100, 1, shape));
            }

            // WHY: Dense candidates including Self exercise the zero-edge fast path. Excluding Self also
            // qualifies the ordered strict-ancestor search; rejection must not fall back to the root.
            foreach (var shape in new[] { "NearestStrictAccept", "NearestStrictReject" })
            {
                observations.Add(await MeasureDeepProfileAsync(database, engine, depth, 100, 1, shape));
            }

            foreach (var anchors in new[] { 10, 100 })
            {
                // WHY: Keep high overlap separate from the full fact/depth matrix. Scaling selective facts
                // inversely with depth observes C*D without silently requesting a billion direct-edge proofs.
                var overlappingFacts = 100_000 / depth;
                observations.Add(await MeasureDeepProfileAsync(
                    database, engine, depth, overlappingFacts, anchors, "Overlap"));
            }
        }

        return observations;
    }

    /// <summary>Measures a deep scalar aggregate against independent known chain adjacency.</summary>
    private static async Task<CompositionCapacityObservation> MeasureDeepProfileAsync(
        TestDatabase database,
        string engine,
        int depth,
        int facts,
        int anchors,
        string shape
    )
    {
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        var commandProbe = new CompositionCommandProbe();
        await using var context = database.CreateContext(planProbe, commandProbe);
        var hierarchy = context.NestedSet<TreeNode>().ForScope(1).InTree(Guid.Empty);
        var lastFact = depth + 1;
        var firstFact = lastFact - facts + 1;
        var pairs = hierarchy.SelectSubtrees(anchor => anchor.NodeId <= anchors,
            (anchor, node, distance) => new { Anchor = anchor, Node = node, Distance = distance });

        var payload = context.Set<UnrelatedRow>().Where(row => row.Id >= firstFact && row.Id <= lastFact);
        var source = pairs.Join(payload, row => row.Node.NodeId, row => row.Id,
            (row, fact) => new { row.Anchor, row.Node, row.Distance, FactKey = fact.Id });

        var paths = shape is "Path" or "Overlap"
            ? hierarchy.WherePathEdges(source, row => row.Anchor, row => row.Node,
                (_, parent, child) => child.Payload != "blocked")
            : source;

        var strict = shape is "NearestStrictAccept" or "NearestStrictReject";
        var reject = shape is "NearestReject" or "NearestStrictReject";
        var filtered = shape == "Path" ? paths
            : hierarchy.WhereNearestAncestorOrSelf(paths, row => row.Node,
                (row, owner) => (shape == "RootOwner" ? owner.NodeId == 1 : owner.Depth >= 0)
                    && (!strict || owner.NodeId != row.Node.NodeId),
                (row, owner) => shape == "NearestStrictAccept"
                    ? owner.NodeId == row.Node.Parent : !reject || owner.NodeId == 1);

        var query = filtered.GroupBy(row => row.Anchor.NodeId)
            .OrderBy(group => group.Key)
            .Select(group => new CompositionCapacityTotal(
                group.Key, group.LongCount(), group.Sum(row => (long)row.FactKey)));

        var constructionCommands = commandProbe.CommandCount;
        var profile = $"chain-depth-{depth}-facts-{facts}-anchors-{anchors}-{shape}";
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

        var expected = reject
            ? []
            : Enumerable.Range(1, anchors)
                .Select(anchor => new CompositionCapacityTotal(
                    anchor, facts, ((long)firstFact + lastFact) * facts / 2))
                .ToArray();

        // WHY: This profile's construction is the same bounded expression shape as the medium fixture;
        // measure that common full filter shape independently instead of allocating the persisted chain.
        var buildBytes = MeasureConstruction(context, int.MaxValue, firstFact);
        var observation = new CompositionCapacityObservation(profile, rows, expected, commands,
            planProbe.ParameterBindings[0].Length,
            constructionCommands, materialized, tracked, buildBytes, elapsed, plan);

        await WriteAsync(engine, observation, planProbe.Commands[0]);

        return observation;
    }
}
