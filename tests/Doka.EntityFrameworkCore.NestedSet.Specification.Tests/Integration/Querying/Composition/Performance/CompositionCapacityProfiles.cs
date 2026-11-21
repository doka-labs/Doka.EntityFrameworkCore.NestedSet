namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Reuses persisted capacity fixtures for query evidence without seeding another million CLR nodes.</summary>
internal static partial class CompositionCapacityProfiles
{
    private const int BalancedBranchSize = 1_000;
    private const int NativeMutationBatchSize = 100_000;

    /// <summary>Measures broad and selective payload joins on an existing valid wide or balanced hierarchy.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="database">The already seeded case-owned database.</param>
    /// <param name="engine">The provider identity used for native plans.</param>
    /// <param name="nodes">The complete persisted population.</param>
    /// <param name="balanced">Whether nodes form balanced bounded branches instead of root children.</param>
    /// <param name="broad">Whether payload covers the full population instead of the last hundred keys.</param>
    /// <returns>Small grouped observations; setup and plan replay are excluded from query timings.</returns>
    internal static async Task<IReadOnlyList<CompositionCapacityObservation>> MeasureAsync(
        TestDatabase database,
        string engine,
        int nodes,
        bool balanced,
        bool broad,
        CancellationToken cancellationToken
    )
    {
        await using var setup = database.CreateContext();
        await SeedPayloadAsync(
            setup,
            nodes,
            broad
                ? nodes
                : Math.Min(nodes, 100),
            cancellationToken: cancellationToken);

        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(
            setup,
            engine,
            cancellationToken: cancellationToken);

        await QueryPlanTestSupport.RefreshStatisticsAsync<UnrelatedRow>(
            setup,
            engine,
            cancellationToken: cancellationToken);

        var results = new List<CompositionCapacityObservation>();

        foreach (var anchors in new[] { 1, 10, 100, 1_000 })
        {
            var planProbe = new EnterpriseProbe(captureParameterBindings: true);
            var commandProbe = new CompositionCommandProbe();
            await using var context = database.CreateContext(planProbe, commandProbe);
            var firstFact = broad
                ? 1
                : Math.Max(1, nodes - 99);

            var firstAnchor = nodes - anchors + 2;
            var buildBytes = MeasureConstruction(context, firstAnchor, firstFact);
            var query = Build(context, firstAnchor, firstFact);
            var profile = $"{(balanced ? "balanced" : "wide")}-{nodes}-anchors-{anchors}-"
                + (broad
                    ? "broad"
                    : "selective");

            // WHY: Only scalar construction is measured above; no anchor, tree, or payload lookup may occur.
            var commandsBefore = commandProbe.CommandCount;
            var started = Stopwatch.GetTimestamp();
            CompositionCapacityTotal[] rows;

            try
            {
                rows = await query.ToArrayAsync(cancellationToken);
            }
            catch (Exception error)
            {
                // WHY: Failure evidence completes independently of the canceled workload and preserves its error.
                await RetainFailureAsync(
                    context,
                    engine,
                    profile,
                    planProbe,
                    error);

                throw;
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            var commands = commandProbe.CommandCount - commandsBefore;
            var materialized = planProbe.MaterializedNodes;
            var tracked = context
                .ChangeTracker
                .Entries()
                .Count();

            string plan;

            try
            {
                plan = await QueryPlanTestSupport.ExplainAsync(
                    context,
                    planProbe,
                    0,
                    engine,
                    cancellationToken: cancellationToken);
            }
            catch (Exception error)
            {
                // WHY: Failure evidence completes independently of the canceled workload and preserves its error.
                await RetainFailureAsync(
                    context,
                    engine,
                    profile,
                    planProbe,
                    error);

                throw;
            }

            var expected = ExpectedTotals(nodes, anchors, firstFact, balanced);
            var observation = new CompositionCapacityObservation(
                profile,
                rows,
                expected,
                commands,
                planProbe.ParameterBindings[0].Length,
                commandsBefore,
                materialized,
                tracked,
                buildBytes,
                elapsed,
                plan);

            results.Add(observation);
            await WriteAsync(engine, observation, planProbe.Commands[0], cancellationToken: cancellationToken);
        }

        if (!broad)
        {
            // WHY: Keep a real upward/downward DTO composition selective; crossing every broad fact with every
            // origin would measure the requested Cartesian result, not accidental whole-tree materialization.
            results.AddRange(
                await MeasureMixedAsync(
                    database,
                    engine,
                    nodes,
                    balanced,
                    cancellationToken: cancellationToken));
        }

        return results;
    }

    /// <summary>Checks operation-only evidence after every measured operation has completed.</summary>
    /// <param name="observations">The measured profiles and their independent adjacency expectations.</param>
    /// <param name="output">The sink retaining observed, non-gating durations.</param>
    internal static void AssertAll(
        IReadOnlyList<CompositionCapacityObservation> observations,
        ITestOutputHelper output
    )
    {
        Assert.NotEmpty(observations);

        foreach (var observation in observations)
        {
            output.WriteLine(
                $"{observation.Profile}; commands={observation.Commands}; parameters={observation.Parameters}; "
                + $"rows={observation.Rows.Length}; "
                + $"entities={observation.Materialized}; construction={observation.BuildBytes}B; "
                + FormattableString.Invariant($"elapsed={observation.Elapsed.TotalMilliseconds:F3}ms"));

            Assert.Equal(observation.Expected, observation.Rows);
            Assert.Equal(0, observation.ConstructionCommands);
            Assert.Equal(1, observation.Commands);
            Assert.InRange(observation.Parameters, 0, 999);
            Assert.Equal(0, observation.Materialized);
            Assert.Equal(0, observation.Tracked);
            Assert.True(observation.BuildBytes > 0);
            Assert.False(string.IsNullOrWhiteSpace(observation.Plan));
        }

        // WHY: Scalar parameter changes must not allocate a tree-sized buffer. The common expression shape
        // permits a small fixed diagnostic variance, independent of selected anchor or persisted node counts.
        foreach (var shape in observations.GroupBy(row => row.Profile.StartsWith("mixed-", StringComparison.Ordinal)))
        {
            Assert.InRange(shape.Max(row => row.BuildBytes) - shape.Min(row => row.BuildBytes), 0, 4_096);
        }
    }

    /// <summary>Derives contributions from the independent parent plan, without consulting stored bounds.</summary>
    private static CompositionCapacityTotal[] ExpectedTotals(
        int nodes,
        int anchors,
        int firstFact,
        bool balanced
    )
    {
        var totals = new SortedDictionary<int, (long Count, long Sum)>();
        var firstAnchor = nodes - anchors + 2;

        for (var fact = firstFact; fact <= nodes; fact++)
        {
            int? current = fact;

            while (current is { } key)
            {
                if (key == 1
                    || key >= firstAnchor)
                {
                    var previous = totals.GetValueOrDefault(key);
                    totals[key] = (previous.Count + 1, previous.Sum + fact);
                }

                current = key == 1
                    ? null
                    : balanced && (key - 2) % BalancedBranchSize != 0
                        ? 2 + (key - 2) / BalancedBranchSize * BalancedBranchSize
                        : 1;
            }
        }

        return totals
            .Select(pair => new CompositionCapacityTotal(pair.Key, pair.Value.Count, pair.Value.Sum))
            .ToArray();
    }

    /// <summary>Persists plans and measurements without making machine-dependent duration a gate.</summary>
    private static Task WriteAsync(
        string engine,
        CompositionCapacityObservation observation,
        string command,
        CancellationToken cancellationToken
    ) => QueryPlanTestSupport.WriteEvidenceAsync(
        engine + "-composition-capacity-" + observation.Profile,
        [
            FormattableString.Invariant(
                $"Profile={observation.Profile}; elapsed={observation.Elapsed.TotalMilliseconds:F3}ms; ")
            + $"commands={observation.Commands}; parameters={observation.Parameters}; rows={observation.Rows.Length}; "
            + $"materialized={observation.Materialized}; tracked={observation.Tracked}; "
            + $"construction={observation.BuildBytes} B/op",
            command,
            observation.Plan,
        ],
        cancellationToken: cancellationToken);
}
