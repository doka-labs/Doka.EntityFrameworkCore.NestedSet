using System.Text.Json;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    private static readonly JsonSerializerOptions s_consumerEvidenceJson = new() { WriteIndented = true };

    /// <summary>Qualifies fact-selected reads without multiplying sources by matching facts.</summary>
    /// <param name="chain">Whether selected sources occur inside a deep chain rather than a broad star.</param>
    /// <param name="inclusive">Whether sources are included as their own ancestors.</param>
    /// <param name="matches">Whether the fact selection finds two distinct nodes or no nodes.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [Trait("Category", "Capacity")]
    public async Task ObserveConsumerFactsAsync_FactSelectedAncestors_RetainBoundedNativeWork(
        bool chain,
        bool inclusive,
        bool matches
    )
    {
        // Arrange
        var scope = s_consumerScope;
        var tree = s_consumerTree;
        var commands = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);

        // WHY: One real database per case prevents prior scopes from changing statistics or physical work.
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<AncestorIndexQueryContext>(
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
            dense: chain,
            cancellationToken: TestContext.Current.CancellationToken,
            branching: false,
            balanced: false,
            nodes: Nodes);

        var selected = chain
            ? new[] { 1_001, 1_002 }
            : [20, 30];

        await SeedConsumerFactsAsync(
            context,
            scope,
            selected,
            cancellationToken: TestContext.Current.CancellationToken,
            unrelatedNode: 99);

        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var label = matches
            ? "selected"
            : "missing";

        var expected = matches
            ? ExpectedConsumerPairs(selected, chain, inclusive)
            : [];

        var profile = $"fact-selection-chain-{chain}-inclusive-{inclusive}-matches-{matches}";

        // Act
        var observation = await ObserveConsumerFactsAsync(
            context,
            commands,
            capture,
            Build,
            expected,
            profile,
            cancellationToken: TestContext.Current.CancellationToken,
            population: Nodes,
            readWarmups: 3,
            measuredReads: 5,
            factRows: 12);

        // Assert
        AssertConsumerFacts(
            observation,
            matches
                ? selected.Length
                : 0);

        return;

        IQueryable<ConsumerFactPair> Build(
            AncestorIndexQueryContext queryContext
        )
        {
            var selection = FactSelection(queryContext, scope, label);
            var hierarchy = queryContext
                .NestedSet<CompositionNavigationNode>()
                .ForScope(scope)
                .InTree(tree);

            var pairs = inclusive
                ? hierarchy.SelectAncestorsOrSelf(
                    selection,
                    (source,
                        owner,
                        distance) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance,
                    })
                : hierarchy.SelectAncestors(
                    selection,
                    (source,
                        owner,
                        distance) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance,
                    });

            return WithConsumerOccurrences(queryContext, scope, pairs);
        }
    }

    /// <summary>Qualifies projected union fact roots without changing hierarchy or occurrence multiplicity.</summary>
    /// <param name="inclusive">Whether sources are included as their own ancestors.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task ObserveConsumerFactsAsync_UnionFactSelectedAncestors_RetainBoundedNativeWork(
        bool inclusive
    )
    {
        // Arrange
        var scope = s_consumerScope;
        var tree = s_consumerTree;
        var commands = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);

        // WHY: One real database per case prevents prior scopes from changing statistics or physical work.
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<AncestorIndexQueryContext>(
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
            cancellationToken: TestContext.Current.CancellationToken,
            dense: false,
            branching: false,
            balanced: false,
            nodes: Nodes);
        int[] selected = [20, 30];
        await SeedConsumerFactsAsync(
            context,
            scope,
            selected,
            cancellationToken: TestContext.Current.CancellationToken,
            unrelatedNode: 99);

        context.AddRange(
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = 13,
                NodeId = selected[0],
                Label = "union-extra",
            },
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = 14,
                NodeId = selected[1],
                Label = "union-extra",
            });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        const string analyze = "ANALYZE TABLE `AncestorIndexQueryItems`";
        await context.Database.ExecuteSqlRawAsync(analyze, CancellationToken.None);
        await context.Database.OpenConnectionAsync(CancellationToken.None);

        var expected = ExpectedConsumerPairs(selected, chain: false, inclusive);
        var profile = $"union-fact-selection-inclusive-{inclusive}";

        // Act
        var observation = await ObserveConsumerFactsAsync(
            context,
            commands,
            capture,
            Build,
            expected,
            profile,
            factRows: 14,
            cancellationToken: TestContext.Current.CancellationToken,
            population: Nodes,
            readWarmups: 3,
            measuredReads: 5);

        // Assert
        AssertConsumerFacts(observation, selected.Length);

        return;

        IQueryable<ConsumerFactPair> Build(
            AncestorIndexQueryContext queryContext
        )
        {
            var selection = UnionFactSelection(queryContext, scope);
            var hierarchy = queryContext
                .NestedSet<CompositionNavigationNode>()
                .ForScope(scope)
                .InTree(tree);

            var pairs = inclusive
                ? hierarchy.SelectAncestorsOrSelf(
                    selection,
                    (source,
                        owner,
                        distance) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance,
                    })
                : hierarchy.SelectAncestors(
                    selection,
                    (source,
                        owner,
                        distance) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance,
                    });

            return WithConsumerOccurrences(queryContext, scope, pairs);
        }
    }

    /// <summary>Qualifies the reviewed million-node, thousand-source gap with one complete native read.</summary>
    /// <param name="inclusive">Whether sources are included as their own ancestors.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task ObserveConsumerFactsAsync_LargeFactSelection_RemainsBounded(
        bool inclusive
    )
    {
        // Arrange
        await using var configured = new TreeContext(ModelCompatibilityDatabase.Options<TreeContext>(Engine));

        // WHY: The typed adapter requires both public APIs. Their absence must fail the native-work
        // qualification instead of skipping the million-node contract.
        var capabilities = NestedSetProviderCapabilities.Resolve(configured);

        Assert.NotNull(capabilities.CompletedSourceMethod);
        Assert.NotNull(capabilities.OrderedJoinMethod);

        const int nodes = 1_000_000;
        const int sources = 1_000;
        var scope = s_consumerScope;
        var tree = s_consumerTree;
        var commands = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);

        // WHY: One real database per case prevents prior scopes from changing statistics or physical work.
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<AncestorIndexQueryContext>(
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
            nodes: nodes,
            cancellationToken: TestContext.Current.CancellationToken,
            dense: false,
            branching: false,
            balanced: false);

        var selected = Enumerable
            .Range(2, sources)
            .ToArray();

        await SeedConsumerFactsAsync(
            context,
            scope,
            selected,
            cancellationToken: TestContext.Current.CancellationToken,
            unrelatedNode: 99);

        await context.Database.OpenConnectionAsync(CancellationToken.None);

        var expected = ExpectedConsumerPairs(selected, chain: false, inclusive);
        var profile = $"large-fact-selection-nodes-{nodes}-sources-{sources}-inclusive-{inclusive}";

        // Act
        var observation = await ObserveConsumerFactsAsync(
            context,
            commands,
            capture,
            Build,
            expected,
            profile,
            population: nodes,
            readWarmups: 0,
            measuredReads: 1,
            factRows: 4 * sources + 4,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertConsumerFacts(observation, sources);

        return;

        IQueryable<ConsumerFactPair> Build(
            AncestorIndexQueryContext queryContext
        )
        {
            var selection = FactSelection(queryContext, scope, "selected");
            var hierarchy = queryContext
                .NestedSet<CompositionNavigationNode>()
                .ForScope(scope)
                .InTree(tree);

            var pairs = inclusive
                ? hierarchy.SelectAncestorsOrSelf(
                    selection,
                    (source,
                        owner,
                        distance) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance,
                    })
                : hierarchy.SelectAncestors(
                    selection,
                    (source,
                        owner,
                        distance) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance,
                    });

            return WithConsumerOccurrences(queryContext, scope, pairs);
        }
    }
}
