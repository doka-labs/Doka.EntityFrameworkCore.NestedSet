using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Checks selected ancestor access with independent adjacency oracles and executed native plans.</summary>
public abstract partial class AncestorIndexWorkTestBase : ProviderTest
{
    private const int Nodes = 100_000;
    private readonly ModelCompatibilityDatabase _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Uses the concrete engine's shared model database and native evidence sink.</summary>
    /// <param name="fixture">The provider-owned model fixture; every test owns a separate scope.</param>
    /// <param name="output">The sink retaining measured native tuple work.</param>
    protected AncestorIndexWorkTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _output = output;
    }

    /// <summary>Rejects selective full-population work and preserves strict and inclusive source occurrences.</summary>
    /// <param name="sources">The number of distinct selected leaves.</param>
    /// <param name="inclusive">Whether every selected leaf must also appear as its own owner.</param>
    /// <param name="forest">Whether unrelated deep trees share the selected scope and traversal coordinates.</param>
    /// <param name="chain">Whether one sparse source selects an early segment of a long unbound chain.</param>
    [Theory]
    [InlineData(1, false, false, false)]
    [InlineData(1_000, false, false, false)]
    [InlineData(1, true, false, false)]
    [InlineData(1_000, true, false, false)]
    [InlineData(1, false, true, false)]
    [InlineData(1, true, true, false)]
    [InlineData(1, false, false, true)]
    [InlineData(1, true, false, true)]
    [Trait("Category", "Capacity")]
    public async Task SelectedAncestorsRetainBoundedNativeWork(int sources, bool inclusive, bool forest, bool chain)
    {
        // Arrange
        // WHY: MariaDB's native plan embeds binary GUID bytes without consistently valid JSON escaping.
        // Printable transport bytes retain actual GUID bindings while keeping the plan valid diagnostic JSON.
        var scope = PrintableGuid();
        var tree = PrintableGuid();
        var commands = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await _fixture.CreateContextAsync<AncestorIndexQueryContext>(
            Engine,
            static options => new AncestorIndexQueryContext(options),
            commands,
            capture);

        await SeedAsync(context, scope, tree, forest, dense: chain);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var selected = chain
            ? [1_001]
            : forest
                ? [1_000]
                : Enumerable
                    .Range(2, sources)
                    .ToArray();

        var hierarchy = context
            .NestedSet<CompositionNavigationNode>()
            .ForScope(scope);

        var hasBinding = NestedSetProviderCapabilities.Resolve(context)
            .CompletedSourceMethod is not null;

        var hasOrderedJoin = NestedSetProviderCapabilities.Resolve(context)
            .OrderedJoinMethod is not null;

        // WHY: Completing a source alone cannot preserve the inclusive source-to-candidate dependency.
        var hasOptimizedPath = hasBinding && (!inclusive || hasOrderedJoin);
        IQueryable<Pair> query;

        if (forest || chain)
        {
            query = inclusive
                ? hierarchy.SelectAncestorsOrSelf(
                    node => Enumerable.Contains(selected, node.Id),
                    (node, owner, distance) => new Pair(node.Id, owner.Id, distance))
                : hierarchy.SelectAncestors(
                    node => Enumerable.Contains(selected, node.Id),
                    (node, owner, distance) => new Pair(node.Id, owner.Id, distance));
        }
        else
        {
            var bounded = hierarchy.InTree(tree);
            query = inclusive
                ? bounded.SelectAncestorsOrSelf(
                    node => Enumerable.Contains(selected, node.Id),
                    (node, owner, distance) => new Pair(node.Id, owner.Id, distance))
                : bounded.SelectAncestors(
                    node => Enumerable.Contains(selected, node.Id),
                    (node, owner, distance) => new Pair(node.Id, owner.Id, distance));
        }

        // WHY: Direct-parent chains supply expected identities without repeating the bounds query.
        var ancestorPairs = chain
            ? Enumerable
                .Range(1, 1_000)
                .Select(owner => new Pair(1_001, owner, 1_001 - owner))
            : forest
                ? Enumerable
                    .Range(1, 999)
                    .Select(owner => new Pair(1_000, owner, 1_000 - owner))
                : selected.Select(key => new Pair(key, 1, 1));

        var expected = (inclusive ? ancestorPairs.Concat(selected.Select(key => new Pair(key, key, 0))) : ancestorPairs)
            .OrderBy(pair => pair.Source)
            .ThenBy(pair => pair.Owner)
            .ToArray();

        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(CompositionNavigationNode))!;

        var flag = (string)entity.FindAnnotation(NestedSetAnnotationNames.AncestorIndexProperty)!.Value!;
        var ancestorIndex = entity
            .GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == flag))
            .GetDatabaseName()!;

        var traversalsBefore = await NativeTraversalCounterAsync(context);
        var indexAttemptsBefore = await MariaSessionCounterAsync(context, "Handler_icp_attempts");
        commands.Reset();
        capture.Commands.Clear();
        capture.ParameterValues.Clear();
        capture.ParameterNames.Clear();
        capture.ParameterBindings.Clear();
        var constructionCommands = commands.CommandCount;

        // Act
        Pair[] actual;

        try
        {
            actual = await query.ToArrayAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // WHY: A native timeout must retain its actual statement even when no executed plan can complete.
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine
                + $"-ancestor-index-failure-forest-{forest}-chain-{chain}-sources-{sources}-inclusive-{inclusive}",
                [
                    exception.GetType()
                        .FullName
                    ?? exception.GetType()
                        .Name,
                    .. capture.Commands,
                    await FailurePlanAsync(context, capture),
                ]);

            throw;
        }

        var commandCount = commands.CommandCount;
        var sql = capture.Commands.Single();
        var indexAttempts = await MariaSessionCounterAsync(context, "Handler_icp_attempts") - indexAttemptsBefore;
        var traversals = await NativeTraversalCounterAsync(context) - traversalsBefore;
        var plan = await QueryPlanTestSupport.ExplainAsync(context, capture, 0, Engine);

        // WHY: Optimized ancestor work is bounded by selected nodes and their actual outputs.
        // A provider without the optional public binding retains its separately labeled legacy scan budget.
        var budget = hasOptimizedPath ? 64L * (sources + expected.Length + 1) : 4L * Nodes * sources;

        // WHY: Keep the native statement and plan even if the native-work reader rejects its diagnostics.
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + $"-ancestor-index-forest-{forest}-chain-{chain}-sources-{sources}-inclusive-{inclusive}",
            [
                $"Nodes={Nodes}; forest={forest}; chain={chain}; sources={sources}; inclusive={inclusive}; "
                + $"sourceBinding={hasBinding}; orderedJoin={hasOrderedJoin}; optimized={hasOptimizedPath}; "
                + $"budget={budget}; resultRows={actual.Length}; indexAttempts={indexAttempts}; traversals={traversals}",
                sql,
                plan,
            ]);

        var work = Engine == "MySql" ? DensePathNativeWork.MySql(plan) : DensePathNativeWork.MariaDb(plan);

        // WHY: ICP can hide a full index scan behind a small post-filter row count in ANALYZE.
        var nativeWork = Math.Max(work.OperatorTuples, Math.Max(indexAttempts, traversals));

        // Assert
        _output.WriteLine($"Forest={forest}; chain={chain}; sources={sources}; inclusive={inclusive}; "
            + $"binding={hasBinding}; orderedJoin={hasOrderedJoin}; optimized={hasOptimizedPath}; "
            + $"operatorTuples={work.OperatorTuples}; indexAttempts={indexAttempts}; traversals={traversals}; budget={budget}.");
        Assert.True(hasOptimizedPath);
        Assert.Equal(expected, actual.OrderBy(pair => pair.Source).ThenBy(pair => pair.Owner));
        Assert.Equal(0, constructionCommands);
        Assert.Equal(1, commandCount);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.True(work.ExecutedOperators > 0);

        if (Engine == "MySql")
        {
            Assert.Equal(actual.Length, work.RootRows);
        }

        Assert.InRange(nativeWork, 0, budget);
        if (hasOptimizedPath)
        {
            // WHY: A semantically correct range query can silently fall back to the old broad scan.
            // Native work and the explicit generated-column predicate qualify the opt-in together.
            Assert.Contains(flag, sql, StringComparison.Ordinal);

            if (!forest && !chain)
            {
                Assert.Contains(ancestorIndex, plan, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Streams dense inclusive paths without materializing an output-sized set-operation boundary.</summary>
    /// <param name="consumerJoin">Whether ordinary application rows join the projected source after hierarchy composition.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task DenseInclusiveAncestorsBoundNativeTemporaryWork(bool consumerJoin)
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
            commands,
            capture);

        await SeedAsync(context, scope, tree, forest: false, dense: true);
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
                    Distance = distance
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
        var traversalsBefore = await NativeTraversalCounterAsync(context);
        var temporaryBefore = await MariaSessionCounterAsync(context, "Handler_tmp_write");
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
        var temporaryAfter = await MariaSessionCounterAsync(context, "Handler_tmp_write");
        var traversals = await NativeTraversalCounterAsync(context) - traversalsBefore;

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
            ]);

        var plan = await QueryPlanTestSupport.ExplainAsync(context, capture, 0, Engine);
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
            ]);

        // Assert
        _output.WriteLine($"Binding={hasBinding}; orderedJoin={hasOrderedJoin}; optimized={hasOptimizedPath}; "
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

    /// <summary>Counts supported native handler-row traversals, including repeated reads of derived and physical rows.</summary>
    private static async Task<long> NativeTraversalCounterAsync(DbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SHOW SESSION STATUS WHERE Variable_name IN "
            + "('Handler_read_first','Handler_read_key','Handler_read_last','Handler_read_next',"
            + "'Handler_read_prev','Handler_read_rnd','Handler_read_rnd_next')";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        long total = 0;
        var counters = 0;

        while (await reader.ReadAsync(CancellationToken.None))
        {
            total = checked(total + Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture));
            counters++;
        }

        return counters != 7
            ? throw new InvalidOperationException("The native engine did not expose every required handler-row traversal counter.")
            : total;
    }

    /// <summary>Retains a planning-only diagnostic without repeating a timed-out native statement.</summary>
    private static async Task<string> FailurePlanAsync(DbContext context, EnterpriseProbe capture)
    {
        try
        {
            await using var command = context
                .Database
                .GetDbConnection()
                .CreateCommand();

            command.CommandText = "EXPLAIN FORMAT=JSON " + capture.Commands.Single();

            foreach (var parameter in capture.ParameterBindings.Single())
            {
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            await reader.ReadAsync(CancellationToken.None);

            return reader.GetString(0);
        }
        catch (Exception exception)
        {
            // WHY: A secondary diagnostic failure must preserve the original native failure reported by the test.
            return "Static native plan unavailable: "
                + exception.GetType().FullName;
        }
    }

    /// <summary>Reads a native session-work counter on the same open measured connection.</summary>
    private async Task<long> MariaSessionCounterAsync(DbContext context, string name)
    {
        if (Engine != "MariaDb")
        {
            return 0;
        }

        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SHOW SESSION STATUS LIKE @name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = name;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        if (!await reader.ReadAsync(CancellationToken.None))
        {
            throw new InvalidOperationException("MariaDB did not expose the requested native work counter.");
        }

        return Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
    }

    /// <summary>Counts executed MySQL materialization rows, independently of legitimate streamed output size.</summary>
    private static long MaterializedTuples(string plan)
    {
        var matches = MaterializedTuplesRegex().Matches(plan);

        if (matches.Count == 0)
        {
            throw new InvalidOperationException("The executed MySQL plan did not expose its materialization operators.");
        }

        return checked((long)matches.Sum(match =>
            double.Parse(match.Groups["rows"].Value, CultureInfo.InvariantCulture)
            * double.Parse(match.Groups["loops"].Value, CultureInfo.InvariantCulture)));
    }

    /// <summary>Seeds independently derived stars or chains without tracking a population of CLR entities.</summary>
    private static async Task SeedAsync(
        AncestorIndexQueryContext context,
        Guid scope,
        Guid tree,
        bool forest,
        bool dense = false,
        bool branching = false,
        bool balanced = false,
        int nodes = Nodes
    )
    {
        var entity = context.Model.FindEntityType(typeof(CompositionNavigationNode))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var helper = context.GetService<ISqlGenerationHelper>();

        var caption = helper.DelimitIdentifier(
            entity.FindNavigation(nameof(CompositionNavigationNode.Details))!.TargetEntityType.FindProperty(
                nameof(CompositionNavigationDetail.Caption))!.GetColumnName(store)!);

        var columns = string.Join(
            ", ",
            new[]
                {
                    nameof(CompositionNavigationNode.Scope),
                    nameof(CompositionNavigationNode.Id),
                    nameof(CompositionNavigationNode.TreeId),
                    nameof(CompositionNavigationNode.ParentId),
                    nameof(CompositionNavigationNode.Left),
                    nameof(CompositionNavigationNode.Right),
                    nameof(CompositionNavigationNode.Depth),
                    nameof(CompositionNavigationNode.Position),
                    nameof(CompositionNavigationNode.Visible),
                }
                .Select(Column)
                .Append(caption));

        var table = helper.DelimitIdentifier(store.Name, store.Schema);
        await using var parameterCommand = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        nodes = branching || balanced ? 100_001 : nodes;
        var balancedCoordinates = balanced ? BalancedCoordinates(nodes) : default;

        for (var start = 1; start <= nodes; start += 1_000)
        {
            var sql = new StringBuilder($"INSERT INTO {table} ({columns}) VALUES ");

            for (var id = start; id < Math.Min(start + 1_000, nodes + 1); id++)
            {
                if (id != start)
                {
                    sql.Append(',');
                }

                var depth = dense
                    ? id - 1
                    : forest
                        ? id - start
                        : id == 1
                            ? 0
                            : 1;

                var parent = dense
                    ? id == 1 ? "NULL" : (id - 1).ToString(CultureInfo.InvariantCulture)
                    : forest
                        ? id == start ? "NULL" : (id - 1).ToString(CultureInfo.InvariantCulture)
                        : id == 1
                            ? "NULL"
                            : "1";

                var left = dense
                    ? id
                    : forest
                        ? depth + 1L
                        : id == 1
                            ? 1
                            : (2L * id) - 2;

                var right = dense
                    ? (2L * nodes) - depth
                    : forest
                        ? 2_000L - depth
                        : id == 1
                            ? 2L * nodes
                            : left + 1;

                var position = dense || forest ? 0 : Math.Max(0, id - 2);

                if (branching)
                {
                    var offset = id == 1 ? 0 : (id - 2) % 1_000;
                    var branch = id == 1 ? 0 : (id - 2) / 1_000;
                    depth = id == 1 ? 0 : offset + 1;
                    parent = id == 1
                        ? "NULL"
                        : offset == 0
                            ? "1"
                            : (id - 1).ToString(CultureInfo.InvariantCulture);

                    left = id == 1 ? 1 : 2L + (2_000L * branch) + offset;
                    right = id == 1 ? 200_002 : 2_001L + (2_000L * branch) - offset;
                    position = offset == 0 ? branch : 0;
                }

                if (balanced)
                {
                    depth = balancedCoordinates.Depths![id];
                    parent = id == 1 ? "NULL" : (id / 2).ToString(CultureInfo.InvariantCulture);
                    left = balancedCoordinates.Lefts![id];
                    right = balancedCoordinates.Rights![id];
                    position = id == 1 ? 0 : id % 2;
                }

                sql.Append(
                    CultureInfo.InvariantCulture,
                    $"(@scope,{id},@tree,{parent},{left},{right},{depth},{position},1,'fixture')");
            }

            var scopeParameter = entity.FindProperty(nameof(CompositionNavigationNode.Scope))!
                .GetRelationalTypeMapping()
                .CreateParameter(parameterCommand, "scope", scope, nullable: false);

            var treeParameter = entity.FindProperty(nameof(CompositionNavigationNode.TreeId))!
                .GetRelationalTypeMapping()
                .CreateParameter(
                    parameterCommand,
                    "tree",
                    forest && start > 1 ? PrintableGuid() : tree,
                    nullable: false);

            // WHY: Typed mappings retain the provider's GUID transport and only fixture cardinalities
            // enter literal SQL. Batches bound setup memory; the generated flag is never assigned.
            await context.Database.ExecuteSqlRawAsync(
                sql.ToString(),
                [scopeParameter, treeParameter],
                CancellationToken.None);
        }

        var analyze = "ANALYZE TABLE " + table;

        await context.Database.ExecuteSqlRawAsync(analyze, CancellationToken.None);

        return;

        string Column(
            string name
        ) => helper.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(store)!);
    }

    /// <summary>Keeps provider-bound GUID plan diagnostics valid on both native JSON writers.</summary>
    private static Guid PrintableGuid() => new(
        Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16]));

    /// <summary>Retains scalar identities and distance without materializing hierarchy payload.</summary>
    private sealed record Pair(
        int Source,
        int Owner,
        int Distance
    );

    [GeneratedRegex(
        @"(?im)^.*(?:materializ|temporary table).*\(actual time=[^)]*rows=(?<rows>[0-9.]+) loops=(?<loops>[0-9.]+)",
        RegexOptions.None,
        "en-DE")]
    private static partial Regex MaterializedTuplesRegex();
}
