using System.Collections;
using System.Globalization;
using System.Text;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>Rejects repeated source-mode scans when independent application rows drive dense ancestor results.</summary>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task DenseConsumerJoinRetainsPointSourceLookup()
    {
        // Arrange
        const int nodes = 100_001;
        const int sources = 1_000;
        const long expected = 1_500_500;
        const long traversalBudget = 64L * (sources + expected + 1);
        var commands = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await _fixture.CreateContextAsync<AncestorIndexConsumerContext>(
            Engine,
            static options => new AncestorIndexConsumerContext(options),
            commands,
            capture);

        await SeedConsumerChainAsync(context, nodes);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var selected = Enumerable
            .Range(1_001, sources)
            .ToArray();

        var pairs = context
            .NestedSet<AncestorIndexConsumerNode>()
            .ForScope(1)
            .SelectAncestorsOrSelf(
                node => Enumerable.Contains(selected, node.Id),
                (node, owner, distance) => new
                {
                    Source = node.Id,
                    Owner = owner.Id,
                    Distance = distance,
                });

        var query = pairs.Join(
            context
                .Set<AncestorIndexConsumerOccurrence>()
                .AsNoTracking()
                .Where(occurrence => occurrence.Selection == "dense"),
            pair => pair.Source,
            occurrence => occurrence.Node,
            (
                pair,
                occurrence
            ) => new
            {
                Occurrence = occurrence.Id,
                pair.Source,
                pair.Owner,
                pair.Distance
            });

        var hasBinding = NestedSetProviderCapabilities.Resolve(context)
            .CompletedSourceMethod is not null;

        var hasOrderedJoin = NestedSetProviderCapabilities.Resolve(context)
            .OrderedJoinMethod is not null;

        var hasOptimizedPath = hasBinding && hasOrderedJoin;
        var seen = new BitArray(checked((int)expected));
        var traversalsBefore = await NativeTraversalCounterAsync(context);
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
            var offset = ((long)sourceIndex * 1_001) + ((long)sourceIndex * (sourceIndex - 1) / 2);
            var slot = offset + pair.Owner - 1;

            if (sourceIndex is < 0 or >= sources
                || pair.Occurrence != pair.Source
                || pair.Owner < 1
                || pair.Owner > pair.Source
                || pair.Distance != pair.Source - pair.Owner
                || slot < 0
                || slot >= seen.Length
                || seen[(int)slot])
            {
                throw new InvalidOperationException(
                    "The consumer adjacency oracle rejected an invalid or duplicated source occurrence.");
            }

            seen[(int)slot] = true;
            count++;
        }

        var commandCount = commands.CommandCount;
        var sql = capture.Commands.Single();
        var traversals = await NativeTraversalCounterAsync(context) - traversalsBefore;

        // WHY: Plan replay is a separate execution. Its failure must not erase the already
        // completed occurrence oracle or the measured original command's session work.
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-ancestor-index-dense-consumer-source-lookup-completed",
            [
                $"Nodes={nodes}; sources={sources}; binding={hasBinding}; orderedJoin={hasOrderedJoin}; "
                + $"optimized={hasOptimizedPath}; expected={expected}; actual={count}; "
                + $"commands={commandCount}; traversals={traversals}; budget={traversalBudget}",
                sql,
            ]);

        // WHY: A failed work bound already proves the regression. Repeating that slow query merely
        // to collect ANALYZE would add another full source-mode scan; retain its static plan instead.
        var executedPlan = hasOptimizedPath && traversals <= traversalBudget;
        var plan = executedPlan
            ? await Specifications.QueryPlanTestSupport.ExplainAsync(context, capture, 0, Engine)
            : await FailurePlanAsync(context, capture);

        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-ancestor-index-dense-consumer-source-lookup",
            [
                $"Nodes={nodes}; sources={sources}; binding={hasBinding}; orderedJoin={hasOrderedJoin}; "
                + $"optimized={hasOptimizedPath}; expected={expected}; actual={count}; "
                + $"commands={commandCount}; traversals={traversals}; budget={traversalBudget}; executedPlan={executedPlan}",
                sql,
                plan,
            ]);

        // Assert
        _output.WriteLine($"Binding={hasBinding}; orderedJoin={hasOrderedJoin}; optimized={hasOptimizedPath}; "
            + $"rows={count}; traversals={traversals}; budget={traversalBudget}.");
        Assert.True(hasOptimizedPath);
        Assert.Equal(expected, count);
        Assert.Equal(1, commandCount);
        Assert.Empty(context.ChangeTracker.Entries());

        Assert.InRange(traversals, 0, traversalBudget);
    }

    /// <summary>Loads a valid chain and independent application occurrences in bounded SQL batches.</summary>
    private static async Task SeedConsumerChainAsync(
        AncestorIndexConsumerContext context,
        int nodes
    )
    {
        for (var start = 1; start <= nodes; start += 1_000)
        {
            var sql = new StringBuilder(
                "INSERT INTO `AncestorIndexConsumerNodes` "
                + "(`Id`,`Scope`,`Tree`,`Parent`,`Left`,`Right`,`Depth`,`Position`) VALUES ");

            for (var id = start; id < Math.Min(start + 1_000, nodes + 1); id++)
            {
                if (id != start)
                {
                    sql.Append(',');
                }

                sql.Append(
                    CultureInfo.InvariantCulture,
                    $"({id},1,1,{(id == 1 ? "NULL" : (id - 1).ToString(CultureInfo.InvariantCulture))},"
                    + $"{id},{(2L * nodes) - id + 1},{id - 1},0)");
            }

            await context.Database.ExecuteSqlRawAsync(sql.ToString(), CancellationToken.None);
        }

        var occurrences = "INSERT INTO `AncestorIndexConsumerOccurrences` (`Id`,`Node`,`Selection`) VALUES "
            + string.Join(
                ',',
                Enumerable
                    .Range(1_001, 1_000)
                    .Select(id => string.Create(CultureInfo.InvariantCulture, $"({id},{id},'dense')")))
            + ", (1,1001,'sparse')";

        await context.Database.ExecuteSqlRawAsync(occurrences, CancellationToken.None);
        const string analyze = "ANALYZE TABLE `AncestorIndexConsumerNodes`,`AncestorIndexConsumerOccurrences`";
        await context.Database.ExecuteSqlRawAsync(analyze, CancellationToken.None);
    }
}
