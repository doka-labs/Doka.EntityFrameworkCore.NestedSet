using System.Collections;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>Streams dense inclusive paths without materializing an output-sized set-operation boundary.</summary>
    /// <param name="consumerJoin">
    /// Whether ordinary application rows join the projected source after hierarchy composition.
    /// </param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task SelectAncestorsOrSelf_DenseInclusiveAncestors_BoundNativeTemporaryWork(
        bool consumerJoin
    )
    {
        // Arrange
        const int sources = 1_000;
        const long expected = 1_500_500;
        var scope = PrintableGuid();
        var tree = PrintableGuid();
        var commands = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await _fixture.CreateContextAsync<AncestorIndexQueryContext>(
            Engine,
            static options => new AncestorIndexQueryContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            commands,
            capture);

        await SeedAsync(
            context,
            scope,
            tree,
            forest: false,
            dense: true,
            cancellationToken: TestContext.Current.CancellationToken,
            branching: false,
            balanced: false,
            nodes: Nodes);

        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var selected = Enumerable
            .Range(1_001, sources)
            .ToArray();

        var hasBinding = NestedSetProviderCapabilities.Resolve(context)
            .CompletedSourceMethod is not null;

        var hasOrderedJoin = NestedSetProviderCapabilities.Resolve(context)
            .OrderedJoinMethod is not null;

        var hasOptimizedPath = hasBinding && hasOrderedJoin;
        var query = context
            .NestedSet<CompositionNavigationNode>()
            .ForScope(scope)
            .InTree(tree)
            .SelectAncestorsOrSelf(
                node => Enumerable.Contains(selected, node.Id),
                (node, owner, distance) => new
                {
                    Source = node.Id,
                    Owner = owner.Id,
                    Distance = distance,
                });

        if (consumerJoin)
        {
            context.AddRange(
                selected.Select(key => new CompositionNavigationItem
                {
                    Scope = scope,
                    Id = key,
                    NodeId = key,
                    Label = "source occurrence",
                }));

            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            // WHY: An ordinary consumer join can become the SQL driver after late entity
            // hydration. It must not turn one source lookup into a scan of every source lane.
            query = query.Join(
                context
                    .Set<CompositionNavigationItem>()
                    .AsNoTracking()
                    .Where(item => item.Scope == scope),
                pair => pair.Source,
                item => item.NodeId,
                (pair, _) => pair);
        }

        // WHY: One bit per expected source/owner pair detects duplicates and omissions while the
        // independent chain formula checks identity and distance without retaining result objects.
        var seen = new BitArray(checked((int)expected));
        var traversalsBefore = await NativeTraversalCounterAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var temporaryBefore = await MariaSessionCounterAsync(
            context,
            "Handler_tmp_write",
            cancellationToken: TestContext.Current.CancellationToken);

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
            var sourceIndex = pair.Source - 1_001;
            var offset = (long)sourceIndex * 1_001 + (long)sourceIndex * (sourceIndex - 1) / 2;
            var slot = offset + pair.Owner - 1;

            if (sourceIndex is < 0 or >= sources
                || pair.Owner < 1
                || pair.Owner > pair.Source
                || pair.Distance != pair.Source - pair.Owner
                || slot < 0
                || slot >= seen.Length
                || seen[(int)slot])
            {
                throw new InvalidOperationException(
                    "The dense adjacency oracle rejected a duplicated or invalid ancestor occurrence.");
            }

            seen[(int)slot] = true;
            count++;
        }

        var commandCount = commands.CommandCount;
        var sql = capture.Commands.Single();
        var temporaryAfter = await MariaSessionCounterAsync(
            context,
            "Handler_tmp_write",
            cancellationToken: TestContext.Current.CancellationToken);

        var traversals = await NativeTraversalCounterAsync(
                context,
                cancellationToken: TestContext.Current.CancellationToken)
            - traversalsBefore;

        // WHY: Executed-plan collection is an independent query. Its timeout must not erase
        // the complete streamed oracle result or the original session-work observations.
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + $"-ancestor-index-dense-inclusive-completed-consumer-{consumerJoin}",
            [
                $"Nodes={Nodes}; sources={sources}; binding={hasBinding}; orderedJoin={hasOrderedJoin}; "
                + $"optimized={hasOptimizedPath}; consumerJoin={consumerJoin}; "
                + $"expected={expected}; actual={count}; commands={commandCount}; traversals={traversals}; "
                + $"temporaryWrites={temporaryAfter - temporaryBefore}",
                sql,
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        var plan = await QueryPlanTestSupport.ExplainAsync(
            context,
            capture,
            0,
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var temporaryWork = Engine == "MariaDb"
            ? temporaryAfter - temporaryBefore
            : hasOptimizedPath
                ? MaterializedTuples(plan)
                : 0;

        // WHY: Temporary structural work scales with source lanes and candidate owners, rather
        // than the 1.5 million valid output pairs. The typed adapter must retain this qualified path.
        var budget = 64L * (sources + 2_000 + 1);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + $"-ancestor-index-dense-inclusive-consumer-{consumerJoin}",
            [
                $"Nodes={Nodes}; sources={sources}; binding={hasBinding}; orderedJoin={hasOrderedJoin}; "
                + $"optimized={hasOptimizedPath}; consumerJoin={consumerJoin}; expected={expected}; "
                + $"actual={count}; temporaryWork={temporaryWork}; budget={budget}; traversals={traversals}",
                sql,
                plan,
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _output.WriteLine(
            $"Binding={hasBinding}; orderedJoin={hasOrderedJoin}; optimized={hasOptimizedPath}; "
            + $"rows={count}; temporaryWork={temporaryWork}; budget={budget}.");

        // WHY: Exact cardinality plus the no-duplicate/range oracle proves every expected bit was visited.
        Assert.True(hasOptimizedPath);
        Assert.Equal(expected, count);
        Assert.Equal(1, commandCount);
        Assert.Empty(context.ChangeTracker.Entries());

        if (hasOptimizedPath)
        {
            Assert.InRange(temporaryWork, 0, budget);
            Assert.InRange(traversals, 0, 64L * (sources + expected + 1));
        }
    }
}
