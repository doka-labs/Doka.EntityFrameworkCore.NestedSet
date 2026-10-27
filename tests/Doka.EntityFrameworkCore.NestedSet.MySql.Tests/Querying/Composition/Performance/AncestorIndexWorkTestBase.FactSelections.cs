using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.Json;
using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Storage;

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
    public async Task FactSelectedAncestorsRetainBoundedNativeWork(
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
            commands,
            capture);

        await SeedAsync(context, scope, tree, forest: false, dense: chain);
        var selected = chain ? new[] { 1_001, 1_002 } : [20, 30];
        await SeedConsumerFactsAsync(context, scope, selected);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var label = matches ? "selected" : "missing";

        var expected = matches ? ExpectedConsumerPairs(selected, chain, inclusive) : [];
        var profile = $"fact-selection-chain-{chain}-inclusive-{inclusive}-matches-{matches}";

        // Act
        var observation = await ObserveConsumerFactsAsync(context, commands, capture, Build, expected, profile);

        // Assert
        AssertConsumerFacts(observation, matches ? selected.Length : 0);
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
                    (
                        source,
                        owner,
                        distance
                    ) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance,
                    })
                : hierarchy.SelectAncestors(
                    selection,
                    (
                        source,
                        owner,
                        distance
                    ) => new ConsumerHierarchyPair
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
    public async Task UnionFactSelectedAncestorsRetainBoundedNativeWork(
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
            commands,
            capture);

        await SeedAsync(context, scope, tree, forest: false);
        int[] selected = [20, 30];
        await SeedConsumerFactsAsync(context, scope, selected);
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
            factRows: 14);

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
                    (
                        source,
                        owner,
                        distance
                    ) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance
                    })
                : hierarchy.SelectAncestors(
                    selection,
                    (
                        source,
                        owner,
                        distance
                    ) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance
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
    public async Task LargeFactSelectionRemainsBounded(
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
            commands,
            capture);

        await SeedAsync(context, scope, tree, forest: false, nodes: nodes);
        var selected = Enumerable
            .Range(2, sources)
            .ToArray();

        await SeedConsumerFactsAsync(context, scope, selected);
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
            factRows: (4 * sources) + 4);

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
                    (
                        source,
                        owner,
                        distance
                    ) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance
                    })
                : hierarchy.SelectAncestors(
                    selection,
                    (
                        source,
                        owner,
                        distance
                    ) => new ConsumerHierarchyPair
                    {
                        Source = source.Id,
                        Owner = owner.Id,
                        Distance = distance
                    });

            return WithConsumerOccurrences(queryContext, scope, pairs);
        }
    }

    /// <summary>Selects the source with ordinary captured fact LINQ that EF normalizes at execution.</summary>
    private static Expression<Func<CompositionNavigationNode, bool>> FactSelection(
        AncestorIndexQueryContext context,
        Guid scope,
        string label
    )
    {
        var facts = context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == label);

        // WHY: Native work evidence must cover ordinary consumer LINQ, including EF's deferred
        // capture expansion, rather than a manually embedded root that bypasses that public spelling.
        return source => facts.Any(fact => fact.NodeId == source.Id);
    }

    /// <summary>Selects through three captured projected fact roots using the sample's UNION ALL shape.</summary>
    private static Expression<Func<CompositionNavigationNode, bool>> UnionFactSelection(
        AncestorIndexQueryContext context,
        Guid scope
    )
    {
        var selected = context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == "selected")
            .Select(fact => new ConsumerFactKey { Node = fact.NodeId });

        var occurrences = context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == "occurrence")
            .Select(fact => new ConsumerFactKey { Node = fact.NodeId });

        var extra = context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == "union-extra")
            .Select(fact => new ConsumerFactKey { Node = fact.NodeId });

        var facts = selected
            .Concat(occurrences)
            .Concat(extra);

        // WHY: UNION ALL retains duplicate facts, while Any selects each hierarchy source once.
        // The unchanged work oracle now also guards EF-normalized projected captures.
        return source => facts.Any(fact => fact.Node == source.Id);
    }

    /// <summary>Retains independent application occurrences after the hierarchy has selected source nodes.</summary>
    private static IQueryable<ConsumerFactPair> WithConsumerOccurrences(
        AncestorIndexQueryContext context,
        Guid scope,
        IQueryable<ConsumerHierarchyPair> pairs
    ) => pairs.Join(
        context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == "occurrence"),
        pair => pair.Source,
        fact => fact.NodeId,
        (pair, fact) => new ConsumerFactPair(fact.Id, pair.Source, pair.Owner, pair.Distance));

    /// <summary>Seeds duplicate selection facts and distinct output occurrences without hierarchy payload.</summary>
    private static async Task SeedConsumerFactsAsync(
        AncestorIndexQueryContext context,
        Guid scope,
        int[] selected,
        int unrelatedNode = 99
    )
    {
        for (var index = 0; index < selected.Length; index++)
        {
            var node = selected[index];
            var offset = 4 * index;
            context.AddRange(
                new CompositionNavigationItem
                {
                    Scope = scope,
                    Id = offset + 1,
                    NodeId = node,
                    Label = "selected",
                },
                new CompositionNavigationItem
                {
                    Scope = scope,
                    Id = offset + 2,
                    NodeId = node,
                    Label = "selected",
                },
                new CompositionNavigationItem
                {
                    Scope = scope,
                    Id = offset + 3,
                    NodeId = node,
                    Label = "occurrence",
                },
                new CompositionNavigationItem
                {
                    Scope = scope,
                    Id = offset + 4,
                    NodeId = node,
                    Label = "occurrence",
                });
        }

        var end = 4 * selected.Length;
        context.AddRange(
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = end + 1,
                NodeId = unrelatedNode,
                Label = "unrelated",
            },
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = end + 2,
                NodeId = 1,
                Label = "owner",
            },
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = end + 3,
                NodeId = 1,
                Label = "owner",
            },
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = end + 4,
                NodeId = selected[0],
                Label = "owner",
            });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        const string analyze = "ANALYZE TABLE `AncestorIndexQueryItems`";
        await context.Database.ExecuteSqlRawAsync(analyze, CancellationToken.None);
    }

    /// <summary>Derives occurrences and distances from the parent rule rather than stored bounds.</summary>
    private static ConsumerFactPair[] ExpectedConsumerPairs(
        int[] selected,
        bool chain,
        bool inclusive
    )
    {
        var pairs = selected.SelectMany((source, index) =>
        {
            var owners = chain
                ? Enumerable.Range(1, inclusive ? source : source - 1)
                : inclusive
                    ? [1, source]
                    : Enumerable.Range(1, 1);

            return owners.SelectMany(owner => Enumerable
                .Range(3 + 4 * index, 2)
                .Select(occurrence => new ConsumerFactPair(
                    occurrence,
                    source,
                    owner,
                    chain
                        ? source - owner
                        : source == owner
                            ? 0
                            : 1)));
        });

        return OrderConsumerPairs(pairs);
    }

    /// <summary>Compares occurrence bags without imposing an undocumented SQL ordering contract.</summary>
    private static ConsumerFactPair[] OrderConsumerPairs(
        IEnumerable<ConsumerFactPair> pairs
    ) => pairs
        .OrderBy(pair => pair.Occurrence)
        .ThenBy(pair => pair.Source)
        .ThenBy(pair => pair.Owner)
        .ThenBy(pair => pair.Distance)
        .ToArray();

    /// <summary>Observes fixed warmup and complete reads; elapsed values are evidence, never gates.</summary>
    private async Task<ConsumerFactObservation> ObserveConsumerFactsAsync(
        AncestorIndexQueryContext context,
        CompositionCommandProbe commands,
        EnterpriseProbe capture,
        Func<AncestorIndexQueryContext, IQueryable<ConsumerFactPair>> build,
        ConsumerFactPair[] expected,
        string profile,
        int population = Nodes,
        int readWarmups = 3,
        int measuredReads = 5,
        int factRows = 12
    )
    {
        var profiling = ConsumerAllocationProfilingEnabled();
        // WHY: Allocation-stack sampling needs more than five reads. Only the separate diagnostic
        // runner enables this switch; ordinary CI/Rider windows and single-read large cases stay unchanged.
        var profileReadWindow = profiling && readWarmups == 3 && measuredReads == 5;

        if (profileReadWindow)
        {
            measuredReads = 512;
        }

        await using var versionCommand = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        versionCommand.CommandText = "SELECT VERSION()";
        var nativeVersion = (string)(await versionCommand.ExecuteScalarAsync(CancellationToken.None))!;
        var providerAssembly = context
            .GetService<IRelationalConnection>()
            .GetType()
            .Assembly;

        var nestedSetAssembly = typeof(NestedSetProviderCapabilities).Assembly;
        var adapterAssembly = NestedSetProviderCapabilities
            .Resolve(context)
            .GetType()
            .Assembly;

        var providerBytes = await File.ReadAllBytesAsync(providerAssembly.Location, CancellationToken.None);
        var nestedSetBytes = await File.ReadAllBytesAsync(nestedSetAssembly.Location, CancellationToken.None);
        var adapterBytes = await File.ReadAllBytesAsync(adapterAssembly.Location, CancellationToken.None);
        var providerHash = Convert.ToHexStringLower(SHA256.HashData(providerBytes));
        var nestedSetHash = Convert.ToHexStringLower(SHA256.HashData(nestedSetBytes));
        var adapterHash = Convert.ToHexStringLower(SHA256.HashData(adapterBytes));

        // WHY: Exact physical counts and native checksums expose contamination even if a scope-filtered
        // oracle would still pass. Paired binaries receive the same deterministic rows and table definitions.
        var physicalNodes = await context
            .Set<CompositionNavigationNode>()
            .IgnoreQueryFilters()
            .LongCountAsync(CancellationToken.None);

        var physicalFacts = await context.Set<CompositionNavigationItem>().LongCountAsync(CancellationToken.None);
        Assert.Equal(population, physicalNodes);
        Assert.Equal(factRows, physicalFacts);
        var physicalTables = await ConsumerPhysicalTablesAsync(context);
        Assert.Equal(physicalNodes, physicalTables[0].LogicalRowCount);
        Assert.Equal(physicalFacts, physicalTables[1].LogicalRowCount);
        var observedUtc = DateTimeOffset.UtcNow;

        var profileEvents = NativeReadProfileEvents.Log;

        for (var warmup = 0; warmup < 8; warmup++)
        {
            GC.KeepAlive(MeasuredBuild());
        }

        commands.Reset();
        var constructionStart = Stopwatch.GetTimestamp();
        var constructionBytes = QueryConstructionAllocation.Measure((Func<IQueryable<ConsumerFactPair>>)MeasuredBuild);
        var constructionNanoseconds = Stopwatch.GetElapsedTime(constructionStart)
            .TotalNanoseconds / 100;
        var constructionCommands = commands.CommandCount;
        var query = MeasuredBuild();
        ConsumerFactPair[] actual = [];

        ClearConsumerCapture(commands, capture);

        for (var warmup = 0; warmup < readWarmups; warmup++)
        {
            actual = await ReadConsumerFactsAsync(context, capture, query, profile);
            RequireConsumerOracle(actual, expected);
            ClearConsumerCapture(commands, capture);
        }

        var hasBinding = NestedSetProviderCapabilities.Resolve(context)
            .CompletedSourceMethod is not null;

        var hasOrderedJoin = NestedSetProviderCapabilities.Resolve(context)
            .OrderedJoinMethod is not null;

        var traversalsBefore = await NativeTraversalCounterAsync(context);
        var indexAttemptsBefore = await MariaSessionCounterAsync(context, "Handler_icp_attempts");
        var reads = new ConsumerReadSample[measuredReads];

        // WHY: Process-wide allocation includes provider, reader, materialization and observers across async
        // continuations. Isolated measurement runs make it comparable; it is not a pure NestedSet allocation metric.
        for (var sample = 0; sample < reads.Length; sample++)
        {
            profileEvents.BeginRead(Engine, profile, "observed", sample);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var started = Stopwatch.GetTimestamp();
            actual = await ReadConsumerFactsAsync(context, capture, query, profile);
            var elapsed = Stopwatch.GetElapsedTime(started);
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            profileEvents.EndRead(Engine, profile, "observed", sample);
            RequireConsumerOracle(actual, expected);
            reads[sample] = new ConsumerReadSample(elapsed.TotalMilliseconds, allocated);
        }

        var commandCount = commands.CommandCount;
        var sql = capture.Commands[0];
        var traversals = await NativeTraversalCounterAsync(context) - traversalsBefore;
        var indexAttempts = await MariaSessionCounterAsync(context, "Handler_icp_attempts") - indexAttemptsBefore;
        var summary = new
        {
            Engine,
            NativeVersion = nativeVersion,
            ProviderVersion = providerAssembly.GetName().Version!.ToString(),
            ProviderSha256 = providerHash,
            NestedSetSha256 = nestedSetHash,
            NestedSetAdapterSha256 = adapterHash,
            Profile = profile,
            CaseNodes = population,
            CaseFactRows = factRows,
            PhysicalNodes = physicalNodes,
            PhysicalFactRows = physicalFacts,
            PhysicalTables = physicalTables,
            ObservedUtc = observedUtc,
            HasBinding = hasBinding,
            HasOrderedJoin = hasOrderedJoin,
            ConstructionBytes = constructionBytes,
            ConstructionNanoseconds = constructionNanoseconds,
            ConstructionCommands = constructionCommands,
            ConstructionOperations = 100,
            ReadWarmups = readWarmups,
            MeasuredReads = measuredReads,
            AllocationStackProfiling = profileReadWindow,
            ResultRows = actual.Length,
            ExpectedRows = expected.Length,
            CommandCount = commandCount,
            Reads = reads,
            Traversals = traversals,
            IndexAttempts = indexAttempts,
        };

        // WHY: Persist the complete-read oracle and session work before the separate executed-plan query.
        // A plan timeout must not erase evidence from reads that already reached EOF successfully.
        var serialized = JsonSerializer.Serialize(summary, s_consumerEvidenceJson);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-ancestor-consumer-" + profile + "-completed",
            [serialized, sql]);

        var plan = await QueryPlanTestSupport.ExplainAsync(context, capture, 0, Engine);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-ancestor-consumer-" + profile,
            [serialized, sql, plan]);

        var work = Engine == "MySql" ? DensePathNativeWork.MySql(plan) : DensePathNativeWork.MariaDb(plan);
        await ObserveConsumerReadsWithoutCaptureAsync(
            context,
            capture,
            build,
            expected,
            profile,
            readWarmups,
            measuredReads);

        return new ConsumerFactObservation(
            profile,
            OrderConsumerPairs(actual),
            expected,
            hasBinding && hasOrderedJoin,
            constructionBytes,
            constructionCommands,
            commandCount,
            traversals,
            indexAttempts,
            work,
            context
                .ChangeTracker
                .Entries()
                .Count(),
            population,
            measuredReads);

        IQueryable<ConsumerFactPair> MeasuredBuild() => build(context);
    }

    /// <summary>Preserves the actual command and planning diagnostic if a complete native read fails.</summary>
    private async Task<ConsumerFactPair[]> ReadConsumerFactsAsync(
        AncestorIndexQueryContext context,
        EnterpriseProbe capture,
        IQueryable<ConsumerFactPair> query,
        string profile
    )
    {
        try
        {
            return await query.ToArrayAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine + "-ancestor-consumer-" + profile + "-failure",
                [exception.ToString(), .. capture.Commands, await FailurePlanAsync(context, capture)]);

            throw;
        }
    }

    /// <summary>Fails immediately when a timed read changes identity, multiplicity, distance or completeness.</summary>
    private static void RequireConsumerOracle(
        ConsumerFactPair[] actual,
        ConsumerFactPair[] expected
    )
    {
        if (!OrderConsumerPairs(actual)
                .SequenceEqual(expected))
        {
            throw new InvalidOperationException("The consumer occurrence bag differs from the adjacency oracle.");
        }
    }

    /// <summary>Separates warmup statements from the fixed complete-read sample window.</summary>
    private static void ClearConsumerCapture(
        CompositionCommandProbe commands,
        EnterpriseProbe capture
    )
    {
        commands.Reset();
        capture.Commands.Clear();
        capture.ParameterValues.Clear();
        capture.ParameterNames.Clear();
        capture.ParameterBindings.Clear();
    }

    /// <summary>Applies deterministic work and construction budgets without gating elapsed timings.</summary>
    private void AssertConsumerFacts(
        ConsumerFactObservation observation,
        int sourceCount
    )
    {
        var tuples = Math.Max(
            observation.Work.OperatorTuples,
            Math.Max(
                observation.Traversals / (double)observation.Reads,
                observation.IndexAttempts / (double)observation.Reads));

        // WHY: The typed adapter requires the qualified binding and ordered join. Work must scale
        // with selected source occurrences and result rows, even as the stored population grows.
        var budget = 64L * (sourceCount + observation.Expected.Length + 1);

        _output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{observation.Profile}; population={observation.Population}; "
                + $"boundedPathRequired={observation.BoundedPathRequired}; "
                + $"actualRows={observation.Actual.Length}; "
                + $"nativeTuples={tuples}; budget={budget}; construction={observation.ConstructionBytes} B/op."));

        Assert.True(observation.BoundedPathRequired);
        Assert.Equal(observation.Expected, observation.Actual);
        Assert.Equal(0, observation.ConstructionCommands);
        Assert.Equal(observation.Reads, observation.Commands);
        Assert.Equal(0, observation.Tracked);
        Assert.InRange(observation.ConstructionBytes, 1, 256 * 1_024);
        Assert.True(observation.Work.ExecutedOperators > 0);
        Assert.InRange(tuples, 0, budget);
    }

    /// <summary>Retains stable consumer occurrence identity separately from hierarchy endpoints.</summary>
    private sealed record ConsumerFactPair(
        int Occurrence,
        int Source,
        int Owner,
        int Distance
    );

    /// <summary>Keeps SQL-composable hierarchy projection members available to consumer predicates.</summary>
    private sealed record ConsumerHierarchyPair
    {
        /// <summary>Gets the selected hierarchy source identity.</summary>
        public int Source { get; init; }

        /// <summary>Gets the matching ancestor identity.</summary>
        public int Owner { get; init; }

        /// <summary>Gets the structural distance from source to ancestor.</summary>
        public int Distance { get; init; }
    }

    /// <summary>Projects independent fact sources to one union-compatible node key.</summary>
    private sealed record ConsumerFactKey
    {
        /// <summary>Gets the hierarchy node referenced by the fact.</summary>
        public int Node { get; init; }
    }

    /// <summary>Retains one complete asynchronous read's elapsed time and process-wide managed allocation.</summary>
    private sealed record ConsumerReadSample(
        double Milliseconds,
        long ProcessAllocatedBytes
    );

    /// <summary>Retains deterministic observations until the test's single assertion phase.</summary>
    private sealed record ConsumerFactObservation(
        string Profile,
        ConsumerFactPair[] Actual,
        ConsumerFactPair[] Expected,
        bool BoundedPathRequired,
        long ConstructionBytes,
        int ConstructionCommands,
        int Commands,
        long Traversals,
        long IndexAttempts,
        DensePathNativeWork Work,
        int Tracked,
        int Population,
        int Reads
    );
}
