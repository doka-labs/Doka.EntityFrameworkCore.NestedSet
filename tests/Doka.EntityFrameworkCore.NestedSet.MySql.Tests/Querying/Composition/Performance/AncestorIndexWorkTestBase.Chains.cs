using System.Collections;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Checks native work across independently selected branches of the same tree.</summary>
public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>Streams the exact 501,005 pairs whose broad repeated native ranges previously regressed.</summary>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task MultiBranchInclusiveAncestorsRetainBoundedNativeWork()
    {
        // Arrange
        const int sources = 1_000;
        const long expected = 501_005;
        var scope = PrintableGuid();
        var tree = PrintableGuid();
        var commands = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await _fixture.CreateContextAsync<AncestorIndexQueryContext>(
            Engine,
            static options => new AncestorIndexQueryContext(options),
            commands,
            capture);

        await SeedAsync(context, scope, tree, forest: false, branching: true);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var selected = Enumerable
            .Range(0, sources)
            .Select(index => 2 + (int)((long)index * 99_999 / 999))
            .ToArray();

        var sourceIndices = selected
            .Select((key, index) => (key, index))
            .ToDictionary(pair => pair.key, pair => pair.index);

        var owners = selected
            .Select(key =>
            {
                var chain = new List<int>();

                for (int? owner = key;
                     owner is not null;
                     owner = owner == 1
                         ? null
                         : (owner.Value - 2) % 1_000 == 0
                             ? 1
                             : owner - 1)
                {
                    chain.Add(owner.Value);
                }

                return chain.ToArray();
            })
            .ToArray();

        var seen = owners
            .Select(chain => new BitArray(chain.Length))
            .ToArray();

        var hasBinding = NestedSetProviderCapabilities.Resolve(context)
            .CompletedSourceMethod is not null;

        var hasOrderedJoin = NestedSetProviderCapabilities.Resolve(context)
            .OrderedJoinMethod is not null;

        var hasOptimizedPath = hasBinding && hasOrderedJoin;
        var query = context
            .NestedSet<CompositionNavigationNode>()
            .ForScope(scope)
            .SelectAncestorsOrSelf(
                node => Enumerable.Contains(selected, node.Id),
                (node, owner, distance) => new Pair(node.Id, owner.Id, distance));

        var traversalsBefore = await NativeTraversalCounterAsync(context);
        var indexAttemptsBefore = await MariaSessionCounterAsync(context, "Handler_icp_attempts");
        commands.Reset();
        capture.Commands.Clear();
        capture.ParameterValues.Clear();
        capture.ParameterNames.Clear();
        capture.ParameterBindings.Clear();
        long count = 0;

        // Act
        await foreach (var pair in query
                           .AsAsyncEnumerable()
                           .WithCancellation(CancellationToken.None))
        {
            if (!sourceIndices.TryGetValue(pair.Source, out var source)
                || pair.Distance < 0
                || pair.Distance >= owners[source].Length
                || owners[source][pair.Distance] != pair.Owner
                || seen[source][pair.Distance])
            {
                throw new InvalidOperationException(
                    "The independent multi-branch parent oracle rejected an ancestor occurrence.");
            }

            seen[source][pair.Distance] = true;
            count++;
        }

        var commandCount = commands.CommandCount;
        var sql = capture.Commands.Single();
        var indexAttempts = await MariaSessionCounterAsync(context, "Handler_icp_attempts") - indexAttemptsBefore;
        var traversals = await NativeTraversalCounterAsync(context) - traversalsBefore;
        var completedRead = $"CompletedRows={count}; expectedRows={expected}; commands={commandCount}; "
            + $"sourceBinding={hasBinding}; orderedJoin={hasOrderedJoin}; optimized={hasOptimizedPath}; "
            + $"indexAttempts={indexAttempts}; traversals={traversals}.";

        // WHY: Instrumented EXPLAIN executes the statement again and can fail independently after
        // a fully drained read. Persist that boundary before requesting the separate actual plan.
        _output.WriteLine(completedRead);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-ancestor-index-multi-branch-inclusive-completed-read",
            [completedRead, sql]);

        string plan;

        try
        {
            plan = await Specifications.QueryPlanTestSupport.ExplainAsync(context, capture, 0, Engine);
        }
        catch (Exception exception)
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine + "-ancestor-index-multi-branch-inclusive-plan-failure",
                [
                    completedRead,
                    exception.GetType().FullName
                    ?? exception.GetType().Name,
                    sql,
                    await FailurePlanAsync(context, capture)
                ]);

            throw;
        }

        var work = Engine == "MySql" ? DensePathNativeWork.MySql(plan) : DensePathNativeWork.MariaDb(plan);
        var baseTable = AncestorIndexBaseTableWork.Read(sql, plan, Engine);
        var nativeWork = Math.Max(work.OperatorTuples, Math.Max(indexAttempts, traversals));
        var budget = 64L * (sources + expected + 1);

        // WHY: The exact ordinary-LINQ calibration can compare 100 million source/owner candidates.
        // Temporary C/hash traversal is CPU work, while the unchanged feature bound rejects repeated
        // base-table scans. Combining these different observations would assert a false O(output) law.
        const long operatorBudget = 4L * 100_001 * sources;
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-ancestor-index-multi-branch-inclusive",
            [
                $"Nodes=100001; branches=100; branchLength=1000; sources={sources}; rows={count}; "
                + $"sourceBinding={hasBinding}; orderedJoin={hasOrderedJoin}; optimized={hasOptimizedPath}; "
                + $"nativeWork={nativeWork}; "
                + $"indexAttempts={indexAttempts}; traversals={traversals}; baseTableTuples={baseTable.ObservedTuples}; "
                + $"unexposedPreIndexAccesses={baseTable.UnexposedPreIndexAccesses}; budget={budget}; operatorBudget={operatorBudget}; "
                + $"materializedOutput={work.MaterializedOutput}; unknownMaterializedOutputs={work.UnknownMaterializedOutputs}",
                sql,
                plan,
            ]);

        // Assert
        _output.WriteLine($"Rows={count}; binding={hasBinding}; orderedJoin={hasOrderedJoin}; "
            + $"optimized={hasOptimizedPath}; nativeWork={nativeWork}; "
            + $"traversals={traversals}; baseTableTuples={baseTable.ObservedTuples}; "
            + $"unexposedPreIndexAccesses={baseTable.UnexposedPreIndexAccesses}; budget={budget}; operatorBudget={operatorBudget}.");
        Assert.True(hasOptimizedPath);
        Assert.Equal(expected, owners.Sum(chain => (long)chain.Length));
        Assert.Equal(expected, count);
        Assert.Equal(1, commandCount);
        Assert.Empty(context.ChangeTracker.Entries());

        if (hasOptimizedPath)
        {
            // WHY: A favorable optimizer estimate can conceal the repeated owner scan. Assert the
            // required mapped-owner edge in emitted SQL as well as the unchanged executed-work budget.
            Assert.Equal(2, sql.Split("STRAIGHT_JOIN", StringSplitOptions.None).Length - 1);
            var joins = AncestorIndexHydrationJoins.Read(sql, "AncestorIndexQueryNodes");
            Assert.Equal("INNER JOIN", joins.Source);
            Assert.Equal("STRAIGHT_JOIN", joins.Owner);
            Assert.DoesNotContain("SELECT STRAIGHT_JOIN", sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(work.ExecutedOperators > 0);

        if (Engine == "MySql")
        {
            Assert.Equal((double?)count, work.RootRows);
        }

        Assert.InRange(baseTable.ObservedTuples, 0, budget);
        Assert.InRange(work.OperatorTuples, 0, operatorBudget);

        if (work.MaterializedOutput is { } materialized)
        {
            // WHY: One row per owner plus the two source lanes fits this exact fixture; spooling
            // all 501,005 result pairs would exceed that relation bound even when reads are fast.
            Assert.InRange(materialized, 0, 100_001L + (2L * sources));
        }
    }
}
