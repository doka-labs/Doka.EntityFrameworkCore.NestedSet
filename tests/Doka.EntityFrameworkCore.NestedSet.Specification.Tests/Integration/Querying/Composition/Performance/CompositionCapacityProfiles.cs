namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Reuses persisted capacity fixtures for query evidence without seeding another million CLR nodes.</summary>
internal static partial class CompositionCapacityProfiles
{
    private const int BalancedBranchSize = 1_000;
    private const int NativeMutationBatchSize = 100_000;

    /// <summary>Measures broad and selective payload joins on an existing valid wide or balanced hierarchy.</summary>
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
        bool balanced = false,
        bool broad = false
    )
    {
        await using var setup = database.CreateContext();
        await SeedPayloadAsync(setup, nodes, broad ? nodes : Math.Min(nodes, 100));
        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(setup, engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<UnrelatedRow>(setup, engine);
        var results = new List<CompositionCapacityObservation>();

        foreach (var anchors in new[] { 1, 10, 100, 1_000 })
        {
            var planProbe = new EnterpriseProbe(captureParameterBindings: true);
            var commandProbe = new CompositionCommandProbe();
            await using var context = database.CreateContext(planProbe, commandProbe);
            var firstFact = broad ? 1 : Math.Max(1, nodes - 99);
            var firstAnchor = nodes - anchors + 2;
            var buildBytes = MeasureConstruction(context, firstAnchor, firstFact);
            var query = Build(context, firstAnchor, firstFact);
            var profile = $"{(balanced ? "balanced" : "wide")}-{nodes}-anchors-{anchors}-"
                + (broad ? "broad" : "selective");

            // WHY: Only scalar construction is measured above; no anchor, tree, or payload lookup may occur.
            var commandsBefore = commandProbe.CommandCount;
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
            var commands = commandProbe.CommandCount - commandsBefore;
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

            var expected = ExpectedTotals(nodes, anchors, firstFact, balanced);
            var observation = new CompositionCapacityObservation(
                profile, rows, expected, commands, planProbe.ParameterBindings[0].Length,
                commandsBefore, materialized, tracked, buildBytes, elapsed, plan);

            results.Add(observation);
            await WriteAsync(engine, observation, planProbe.Commands[0]);
        }

        if (!broad)
        {
            // WHY: Keep a real upward/downward DTO composition selective; crossing every broad fact with every
            // origin would measure the requested Cartesian result, not accidental whole-tree materialization.
            results.AddRange(await MeasureMixedAsync(database, engine, nodes, balanced));
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

    /// <summary>Converts an existing wide fixture to equal-sized depth-two branches with bounded last branch.</summary>
    /// <remarks>
    /// WHY: Native arithmetic updates reuse the persisted population, avoid an additional million-node import,
    /// and retain exact adjacency while comparing genuinely different branching distributions.
    /// </remarks>
    internal static async Task<int> BalanceAsync(
        TreeContext context,
        int nodes
    )
    {
        var changed = 0;

        // WHY: Updating every indexed boundary in one million-row statement holds an unbounded fixture command.
        // Primary-key windows keep setup bounded without changing the query timeout or seeding a second tree.
        for (var first = 1; first <= nodes; first += NativeMutationBatchSize)
        {
            var last = Math.Min(nodes, first + NativeMutationBatchSize - 1);
            changed += await context.Set<TreeNode>()
                .Where(node => node.NodeId > 1 && node.NodeId >= first && node.NodeId <= last)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(node => node.Parent,
                        node => (node.NodeId - 2) % BalancedBranchSize == 0
                            ? 1
                            : 2 + ((int)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize)
                                * BalancedBranchSize))
                    .SetProperty(node => node.Start,
                        node => 2L + ((long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize)
                                * 2L * BalancedBranchSize)
                            + ((node.NodeId - 2) % BalancedBranchSize == 0
                                ? 0L
                                : (2L * ((node.NodeId - 2) % BalancedBranchSize)) - 1))
                    // WHY: CASE preserves the partial-branch cap at EF's default SQL Server compatibility;
                    // Math.Min requires a newer LEAST translation and would fail before fixture I/O.
                    .SetProperty(node => node.End,
                        node => (node.NodeId - 2) % BalancedBranchSize == 0
                            ? 1L + (((long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize) + 1L)
                                * 2L * BalancedBranchSize) < (2L * nodes) - 1
                                ? 1L + (((long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize) + 1L)
                                    * 2L * BalancedBranchSize)
                                : (2L * nodes) - 1
                            : 2L + ((long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize)
                                    * 2L * BalancedBranchSize)
                                + (2L * ((node.NodeId - 2) % BalancedBranchSize)))
                    .SetProperty(node => node.Depth,
                        node => (node.NodeId - 2) % BalancedBranchSize == 0 ? 1 : 2)
                    .SetProperty(node => node.Position,
                        node => (node.NodeId - 2) % BalancedBranchSize == 0
                            ? (long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize)
                            : ((node.NodeId - 2) % BalancedBranchSize) - 1),
                    CancellationToken.None);
        }

        return changed;
    }

    /// <summary>Restores wide adjacency and the original repair corruption before its heap observer starts.</summary>
    /// <param name="context">The existing fixture context outside the measured repair operation.</param>
    /// <param name="nodes">The complete existing population; no additional tree is seeded.</param>
    /// <returns>The number of existing rows changed across bounded primary-key windows.</returns>
    internal static async Task<int> CorruptWideForRepairAsync(
        TreeContext context,
        int nodes
    )
    {
        var changed = 0;

        // WHY: Index maintenance for a million-row fixture reset must stay bounded just like native seeding.
        // This occurs before measurement and does not relax the public rebuild's original command or heap limits.
        for (var first = 1; first <= nodes; first += NativeMutationBatchSize)
        {
            var last = Math.Min(nodes, first + NativeMutationBatchSize - 1);
            changed += await context.Set<TreeNode>()
                .Where(node => node.NodeId >= first && node.NodeId <= last)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(node => node.Parent, node => node.NodeId == 1 ? null : (int?)1)
                    .SetProperty(node => node.Position, node => node.NodeId == 1 ? 0L : node.NodeId - 2L)
                    .SetProperty(node => node.Start, 1L)
                    .SetProperty(node => node.End, 2L)
                    .SetProperty(node => node.Depth, 9), CancellationToken.None);
        }

        return changed;
    }

    /// <summary>Builds an anchor-payload aggregate using all three filters on the same composed source.</summary>
    private static IQueryable<CompositionCapacityTotal> Build(
        TreeContext context,
        int firstAnchor,
        int firstFact
    )
    {
        var hierarchy = context.NestedSet<TreeNode>().ForScope(1).InTree(Guid.Empty);
        var pairs = hierarchy.SelectSubtrees(
            anchor => anchor.NodeId == 1 || anchor.NodeId >= firstAnchor,
            (anchor, node, distance) => new { Anchor = anchor, Node = node, Distance = distance });

        var payload = context.Set<UnrelatedRow>().Where(fact => fact.Id >= firstFact);
        var contributions = pairs.Join(payload, row => row.Node.NodeId, fact => fact.Id,
            (row, fact) => new { row.Anchor, row.Node, row.Distance, FactKey = fact.Id });

        var paths = hierarchy.WherePathEdges(contributions,
            row => row.Anchor, row => row.Node, (_, parent, child) => child.Payload != "blocked");

        var any = hierarchy.WhereAnyAncestorOrSelf(paths,
            row => row.Node, (_, owner) => owner.Payload != "blocked");

        var nearest = hierarchy.WhereNearestAncestorOrSelf(any,
            row => row.Node, (_, owner) => owner.Depth >= 0, (_, owner) => owner.Payload != "blocked");

        return nearest.GroupBy(row => row.Anchor.NodeId)
            .OrderBy(group => group.Key)
            .Select(group => new CompositionCapacityTotal(
                group.Key, group.LongCount(), group.Sum(row => (long)row.FactKey)));
    }

    /// <summary>Measures warmed construction only; database population never becomes a CLR collection.</summary>
    private static long MeasureConstruction(
        TreeContext context,
        int firstAnchor,
        int firstFact
    ) => MeasureConstruction(() => Build(context, firstAnchor, firstFact));

    /// <summary>Measures a warmed query factory without translation, I/O, or row-dependent work.</summary>
    private static long MeasureConstruction(
        Func<IQueryable> build
    )
    {
        for (var iteration = 0; iteration < 8; iteration++)
        {
            GC.KeepAlive(build());
        }

        const int operations = 32;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < operations; iteration++)
        {
            GC.KeepAlive(build());
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / operations;
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
                if (key == 1 || key >= firstAnchor)
                {
                    var previous = totals.GetValueOrDefault(key);
                    totals[key] = (previous.Count + 1, previous.Sum + fact);
                }

                current = key == 1 ? null
                    : balanced && (key - 2) % BalancedBranchSize != 0
                        ? 2 + (((key - 2) / BalancedBranchSize) * BalancedBranchSize)
                        : 1;
            }
        }

        return totals.Select(pair => new CompositionCapacityTotal(pair.Key, pair.Value.Count, pair.Value.Sum))
            .ToArray();
    }

    /// <summary>Copies only the requested scalar payload keys with mapped table names and bounded SQL.</summary>
    /// <param name="context">The case-owned database with its already seeded hierarchy.</param>
    /// <param name="nodes">The largest source key in the fixture's dense population.</param>
    /// <param name="facts">The number of trailing hierarchy keys that receive payload.</param>
    /// <returns>A task completing after every bounded insert has finished.</returns>
    internal static async Task SeedPayloadAsync(
        TreeContext context,
        int nodes,
        int facts
    )
    {
        await context.Set<UnrelatedRow>().ExecuteDeleteAsync(CancellationToken.None);
        var sql = context.GetService<ISqlGenerationHelper>();
        var source = context.Model.FindEntityType(typeof(TreeNode))!;
        var target = context.Model.FindEntityType(typeof(UnrelatedRow))!;
        var sourceStore = StoreObjectIdentifier.Table(source.GetTableName()!, source.GetSchema());
        var targetStore = StoreObjectIdentifier.Table(target.GetTableName()!, target.GetSchema());
        var sourceKey = sql.DelimitIdentifier(source.FindProperty(nameof(TreeNode.NodeId))!
            .GetColumnName(sourceStore)!);

        var targetKey = sql.DelimitIdentifier(target.FindProperty(nameof(UnrelatedRow.Id))!
            .GetColumnName(targetStore)!);

        var value = sql.DelimitIdentifier(target.FindProperty(nameof(UnrelatedRow.Value))!
            .GetColumnName(targetStore)!);

        // WHY: A broad ten-million-row fixture must not become one extra unbounded INSERT before
        // the measured query. Reuse the existing primary-key window size without changing facts or timeouts.
        for (var first = (long)nodes - facts + 1; first <= nodes; first += NativeMutationBatchSize)
        {
            var last = Math.Min((long)nodes, first + NativeMutationBatchSize - 1);
            var statement = $"INSERT INTO {sql.DelimitIdentifier(targetStore.Name, targetStore.Schema)} "
                + $"({targetKey}, {value}) SELECT {sourceKey}, 'fact' "
                + $"FROM {sql.DelimitIdentifier(sourceStore.Name, sourceStore.Schema)} "
                + $"WHERE {sourceKey} >= {first.ToString(CultureInfo.InvariantCulture)} "
                + $"AND {sourceKey} <= {last.ToString(CultureInfo.InvariantCulture)}";

            // WHY: Only fixture-owned integer bounds are interpolated; identifiers use the provider delimiter.
            await context.Database.ExecuteSqlRawAsync(statement, CancellationToken.None);
        }
    }

    /// <summary>Persists plans and measurements without making machine-dependent duration a gate.</summary>
    private static Task WriteAsync(
        string engine,
        CompositionCapacityObservation observation,
        string command
    ) => QueryPlanTestSupport.WriteEvidenceAsync(engine + "-composition-capacity-" + observation.Profile,
    [
        FormattableString.Invariant(
            $"Profile={observation.Profile}; elapsed={observation.Elapsed.TotalMilliseconds:F3}ms; ")
            + $"commands={observation.Commands}; parameters={observation.Parameters}; rows={observation.Rows.Length}; "
            + $"materialized={observation.Materialized}; tracked={observation.Tracked}; "
            + $"construction={observation.BuildBytes} B/op",
        command,
        observation.Plan,
    ]);
}
