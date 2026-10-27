using System.Collections;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Guards mapped-owner hydration after selective strict matching in a broad balanced tree.</summary>
public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>Checks exact parent occurrences while rejecting millions of premature owner primary-key reads.</summary>
    /// <param name="treeBound">Whether the same hierarchy is explicitly restricted to its selected tree.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task BalancedStrictAncestorsFilterBeforeOwnerHydration(
        bool treeBound
    )
    {
        // Arrange
        const int nodes = 100_001;
        const int sources = 1_000;
        const long expected = 15_689;
        var scope = PrintableGuid();
        var tree = PrintableGuid();
        var commands = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await _fixture.CreateContextAsync<AncestorIndexQueryContext>(
            Engine,
            static options => new AncestorIndexQueryContext(options),
            commands,
            capture);

        await SeedAsync(context, scope, tree, forest: false, balanced: true);
        var selected = Enumerable
            .Range(0, sources)
            .Select(index => 50_001 + (int)((long)index * 50_000 / (sources - 1)))
            .ToArray();

        context.AddRange(
            selected.Select((key, index) => new CompositionNavigationItem
            {
                Scope = scope,
                Id = index + 1,
                NodeId = key,
                Label = "balanced source",
            }));

        context.Add(
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = sources + 1,
                NodeId = 1,
                Label = "unselected occurrence",
            });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var owners = selected
            .Select(key =>
            {
                var ancestors = new List<int>();

                for (var owner = key / 2; owner > 0; owner /= 2)
                {
                    ancestors.Add(owner);
                }

                return ancestors.ToArray();
            })
            .ToArray();

        var seen = owners
            .Select(ancestors => new BitArray(ancestors.Length))
            .ToArray();

        var hierarchy = context
            .NestedSet<CompositionNavigationNode>()
            .ForScope(scope);

        var pairs = treeBound
            ? hierarchy
                .InTree(tree)
                .SelectAncestors(
                    node => Enumerable.Contains(selected, node.Id),
                    (node, owner, distance) => new
                    {
                        Source = node.Id,
                        Owner = owner.Id,
                        Distance = distance,
                    })
            : hierarchy.SelectAncestors(
                node => Enumerable.Contains(selected, node.Id),
                (node, owner, distance) => new
                {
                    Source = node.Id,
                    Owner = owner.Id,
                    Distance = distance,
                });

        var query = pairs.Join(
            context
                .Set<CompositionNavigationItem>()
                .AsNoTracking()
                .Where(item => item.Scope == scope && item.Label == "balanced source"),
            pair => pair.Source,
            item => item.NodeId,
            (pair, item) => new
            {
                Occurrence = item.Id,
                pair.Source,
                pair.Owner,
                pair.Distance,
            });

        var hasBinding = NestedSetProviderCapabilities.Resolve(context)
            .CompletedSourceMethod is not null;

        var traversalsBefore = await NativeTraversalCounterAsync(context);
        var indexAttemptsBefore = await MariaSessionCounterAsync(context, "Handler_icp_attempts");
        commands.Reset();
        capture.Commands.Clear();
        capture.ParameterValues.Clear();
        capture.ParameterNames.Clear();
        capture.ParameterBindings.Clear();
        long count = 0;

        // Act
        try
        {
            await foreach (var pair in query
                               .AsAsyncEnumerable()
                               .WithCancellation(CancellationToken.None))
            {
                var source = pair.Occurrence - 1;
                var distance = pair.Distance - 1;

                if (source is < 0 or >= sources
                    || selected[source] != pair.Source
                    || distance < 0
                    || distance >= owners[source].Length
                    || owners[source][distance] != pair.Owner
                    || seen[source][distance])
                {
                    throw new InvalidOperationException(
                        "The independent balanced parent/occurrence oracle rejected a pair.");
                }

                seen[source][distance] = true;
                count++;
            }
        }
        catch (Exception exception)
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine + $"-ancestor-index-balanced-strict-tree-{treeBound}-read-failure",
                [
                    $"RowsBeforeFailure={count}; expected={expected}; sourceBinding={hasBinding}",
                    exception.GetType().FullName
                    ?? exception.GetType().Name,
                    .. capture.Commands,
                    await FailurePlanAsync(context, capture)
                ]);

            throw;
        }

        var commandCount = commands.CommandCount;
        var sql = capture.Commands.Single();
        var indexAttempts = await MariaSessionCounterAsync(context, "Handler_icp_attempts") - indexAttemptsBefore;
        var traversals = await NativeTraversalCounterAsync(context) - traversalsBefore;
        var completed = $"CompletedRows={count}; expected={expected}; commands={commandCount}; "
            + $"sourceBinding={hasBinding}; indexAttempts={indexAttempts}; traversals={traversals}.";

        // WHY: Actual-plan replay is a separate execution. Preserve the fully drained result
        // before instrumentation can fail independently or expose only a partial replay.
        _output.WriteLine(completed);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + $"-ancestor-index-balanced-strict-tree-{treeBound}-completed-read",
            [completed, sql]);

        var plan = await Specifications.QueryPlanTestSupport.ExplainAsync(context, capture, 0, Engine);
        var work = Engine == "MySql" ? DensePathNativeWork.MySql(plan) : DensePathNativeWork.MariaDb(plan);
        var baseTable = AncestorIndexBaseTableWork.Read(sql, plan, Engine);
        const long baseBudget = 64L * (sources + expected + 1);
        const long operatorBudget = 4L * nodes * sources;

        // WHY: Broad interval matching can legitimately visit temporary candidates repeatedly.
        // This regression concerns avoidable physical owner lookups before cheap containment,
        // so its base-table bound must not mistake temporary CPU work for physical hydration.
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + $"-ancestor-index-balanced-strict-tree-{treeBound}",
            [
                $"{completed} baseTableTuples={baseTable.ObservedTuples}; baseBudget={baseBudget}; "
                + $"operatorTuples={work.OperatorTuples}; operatorBudget={operatorBudget}; "
                + $"unexposedPreIndexAccesses={baseTable.UnexposedPreIndexAccesses}.",
                sql,
                plan,
            ]);

        // Assert
        _output.WriteLine($"BaseTableTuples={baseTable.ObservedTuples}; baseBudget={baseBudget}; "
            + $"operatorTuples={work.OperatorTuples}; operatorBudget={operatorBudget}.");
        Assert.True(hasBinding);
        Assert.Equal(expected, owners.Sum(ancestors => (long)ancestors.Length));
        Assert.Equal(expected, count);
        Assert.All(seen, occurrences => Assert.All(occurrences.Cast<bool>(), occurrence => Assert.True(occurrence)));
        Assert.Equal(1, commandCount);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.True(work.ExecutedOperators > 0);

        if (Engine == "MySql")
        {
            Assert.Equal((double?)count, work.RootRows);
        }

        Assert.InRange(baseTable.ObservedTuples, 0, baseBudget);
        Assert.InRange(work.OperatorTuples, 0, operatorBudget);
    }

    /// <summary>Builds ordered binary-tree coordinates independently without retaining mapped entities.</summary>
    private static (long[] Lefts, long[] Rights, int[] Depths) BalancedCoordinates(
        int nodes
    )
    {
        var left = new long[nodes + 1];
        var right = new long[nodes + 1];
        var depth = new int[nodes + 1];
        var stack = new Stack<(int Id, bool Exit, int Depth)>();
        stack.Push((1, false, 0));
        long coordinate = 0;

        while (stack.TryPop(out var item))
        {
            if (item.Exit)
            {
                right[item.Id] = ++coordinate;

                continue;
            }

            left[item.Id] = ++coordinate;
            depth[item.Id] = item.Depth;
            stack.Push((item.Id, true, item.Depth));
            var second = (2 * item.Id) + 1;

            if (second <= nodes)
            {
                stack.Push((second, false, item.Depth + 1));
            }

            if (second - 1 <= nodes)
            {
                stack.Push((second - 1, false, item.Depth + 1));
            }
        }

        return (left, right, depth);
    }
}
