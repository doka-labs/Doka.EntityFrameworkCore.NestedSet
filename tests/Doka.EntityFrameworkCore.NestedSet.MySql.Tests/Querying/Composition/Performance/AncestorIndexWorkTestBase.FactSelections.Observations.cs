using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
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
                ? Enumerable.Range(
                    1,
                    inclusive
                        ? source
                        : source - 1)
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
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="commands">The command observation counters.</param>
    /// <param name="capture">The command and materialization capture.</param>
    /// <param name="build">The query factory used for construction and execution observations.</param>
    /// <param name="expected">The independently derived expected results.</param>
    /// <param name="profile">The selected synthetic workload profile.</param>
    /// <param name="population">The expected physical hierarchy population.</param>
    /// <param name="readWarmups">The fixed number of unmeasured warmup reads.</param>
    /// <param name="measuredReads">The fixed number of complete reads inside the observation window.</param>
    /// <param name="factRows">The expected number of synthetic consumer fact rows.</param>
    private async Task<ConsumerFactObservation> ObserveConsumerFactsAsync(
        AncestorIndexQueryContext context,
        CompositionCommandProbe commands,
        EnterpriseProbe capture,
        Func<AncestorIndexQueryContext, IQueryable<ConsumerFactPair>> build,
        ConsumerFactPair[] expected,
        string profile,
        int population,
        int readWarmups,
        int measuredReads,
        int factRows,
        CancellationToken cancellationToken
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
        var nativeVersion = (string)(await versionCommand.ExecuteScalarAsync(cancellationToken))!;
        var providerAssembly = context
            .GetService<IRelationalConnection>()
            .GetType()
            .Assembly;

        var nestedSetAssembly = typeof(NestedSetProviderCapabilities).Assembly;
        var adapterAssembly = NestedSetProviderCapabilities
            .Resolve(context)
            .GetType()
            .Assembly;

        var providerBytes = await File.ReadAllBytesAsync(providerAssembly.Location, cancellationToken);
        var nestedSetBytes = await File.ReadAllBytesAsync(nestedSetAssembly.Location, cancellationToken);
        var adapterBytes = await File.ReadAllBytesAsync(adapterAssembly.Location, cancellationToken);
        var providerHash = Convert.ToHexStringLower(SHA256.HashData(providerBytes));
        var nestedSetHash = Convert.ToHexStringLower(SHA256.HashData(nestedSetBytes));
        var adapterHash = Convert.ToHexStringLower(SHA256.HashData(adapterBytes));

        // WHY: Exact physical counts and native checksums expose contamination even if a scope-filtered
        // oracle would still pass. Paired binaries receive the same deterministic rows and table definitions.
        var physicalNodes = await context
            .Set<CompositionNavigationNode>()
            .IgnoreQueryFilters()
            .LongCountAsync(cancellationToken);

        var physicalFacts = await context
            .Set<CompositionNavigationItem>()
            .LongCountAsync(cancellationToken);

        Assert.Equal(population, physicalNodes);
        Assert.Equal(factRows, physicalFacts);
        var physicalTables = await ConsumerPhysicalTablesAsync(context, cancellationToken: cancellationToken);
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
                .TotalNanoseconds
            / 100;

        var constructionCommands = commands.CommandCount;
        var query = MeasuredBuild();
        ConsumerFactPair[] actual = [];

        ClearConsumerCapture(commands, capture);

        for (var warmup = 0; warmup < readWarmups; warmup++)
        {
            actual = await ReadConsumerFactsAsync(
                context,
                capture,
                query,
                profile,
                cancellationToken: cancellationToken);

            RequireConsumerOracle(actual, expected);
            ClearConsumerCapture(commands, capture);
        }

        var hasBinding = NestedSetProviderCapabilities.Resolve(context)
            .CompletedSourceMethod is not null;

        var hasOrderedJoin = NestedSetProviderCapabilities.Resolve(context)
            .OrderedJoinMethod is not null;

        var traversalsBefore = await NativeTraversalCounterAsync(context, cancellationToken: cancellationToken);
        var indexAttemptsBefore = await MariaSessionCounterAsync(
            context,
            "Handler_icp_attempts",
            cancellationToken: cancellationToken);

        var reads = new ConsumerReadSample[measuredReads];

        // WHY: Process-wide allocation includes provider, reader, materialization and observers across async
        // continuations. Isolated measurement runs make it comparable; it is not a pure NestedSet allocation metric.
        for (var sample = 0; sample < reads.Length; sample++)
        {
            profileEvents.BeginRead(Engine, profile, "observed", sample);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var started = Stopwatch.GetTimestamp();
            actual = await ReadConsumerFactsAsync(
                context,
                capture,
                query,
                profile,
                cancellationToken: cancellationToken);

            var elapsed = Stopwatch.GetElapsedTime(started);
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            profileEvents.EndRead(Engine, profile, "observed", sample);
            RequireConsumerOracle(actual, expected);
            reads[sample] = new ConsumerReadSample(elapsed.TotalMilliseconds, allocated);
        }

        var commandCount = commands.CommandCount;
        var sql = capture.Commands[0];
        var traversals = await NativeTraversalCounterAsync(
                context,
                cancellationToken: cancellationToken)
            - traversalsBefore;

        var indexAttempts = await MariaSessionCounterAsync(
                context,
                "Handler_icp_attempts",
                cancellationToken: cancellationToken)
            - indexAttemptsBefore;

        var summary = new
        {
            Engine,
            NativeVersion = nativeVersion,
            ProviderVersion = providerAssembly.GetName()
                .Version!.ToString(),
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
            [serialized, sql],
            cancellationToken: cancellationToken);

        var plan = await QueryPlanTestSupport.ExplainAsync(
            context,
            capture,
            0,
            Engine,
            cancellationToken: cancellationToken);

        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-ancestor-consumer-" + profile,
            [serialized, sql, plan],
            cancellationToken: cancellationToken);

        var work = Engine == "MySql"
            ? DensePathNativeWork.MySql(plan)
            : DensePathNativeWork.MariaDb(plan);

        await ObserveConsumerReadsWithoutCaptureAsync(
            context,
            capture,
            build,
            expected,
            profile,
            readWarmups,
            measuredReads,
            cancellationToken: cancellationToken);

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
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="capture">The command and materialization capture.</param>
    /// <param name="query">The composed provider query to execute.</param>
    /// <param name="profile">The selected synthetic workload profile.</param>
    private async Task<ConsumerFactPair[]> ReadConsumerFactsAsync(
        AncestorIndexQueryContext context,
        EnterpriseProbe capture,
        IQueryable<ConsumerFactPair> query,
        string profile,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await query.ToArrayAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine + "-ancestor-consumer-" + profile + "-failure",
                [
                    exception.ToString(), .. capture.Commands, await FailurePlanAsync(
                        context,
                        capture,
                        cancellationToken: cancellationToken),
                ],
                cancellationToken: cancellationToken);

            throw;
        }
    }
}
