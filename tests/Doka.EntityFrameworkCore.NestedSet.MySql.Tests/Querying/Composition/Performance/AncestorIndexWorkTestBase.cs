using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;

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
    public async Task WriteEvidenceAsync_SelectedAncestors_RetainBoundedNativeWork(
        int sources,
        bool inclusive,
        bool forest,
        bool chain
    )
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
            cancellationToken: TestContext.Current.CancellationToken,
            commands,
            capture);

        await SeedAsync(
            context,
            scope,
            tree,
            forest,
            dense: chain,
            cancellationToken: TestContext.Current.CancellationToken,
            branching: false,
            balanced: false,
            nodes: Nodes);

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

        var expected = (inclusive
                ? ancestorPairs.Concat(selected.Select(key => new Pair(key, key, 0)))
                : ancestorPairs)
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

        var traversalsBefore = await NativeTraversalCounterAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var indexAttemptsBefore = await MariaSessionCounterAsync(
            context,
            "Handler_icp_attempts",
            cancellationToken: TestContext.Current.CancellationToken);

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
            // WHY: Failure evidence must finish independently of caller cancellation and retain the native statement.
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine
                + $"-ancestor-index-failure-forest-{forest}-chain-{chain}-sources-{sources}-inclusive-{inclusive}",
                [
                    exception.GetType()
                        .FullName
                    ?? exception.GetType()
                        .Name,
                    .. capture.Commands,
                    await FailurePlanAsync(context, capture, cancellationToken: CancellationToken.None),
                ],
                cancellationToken: CancellationToken.None);

            throw;
        }

        var commandCount = commands.CommandCount;
        var sql = capture.Commands.Single();
        var indexAttempts = await MariaSessionCounterAsync(
                context,
                "Handler_icp_attempts",
                cancellationToken: TestContext.Current.CancellationToken)
            - indexAttemptsBefore;

        var traversals = await NativeTraversalCounterAsync(
                context,
                cancellationToken: TestContext.Current.CancellationToken)
            - traversalsBefore;

        var plan = await QueryPlanTestSupport.ExplainAsync(
            context,
            capture,
            0,
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        // WHY: Optimized ancestor work is bounded by selected nodes and their actual outputs.
        // A provider without the optional public binding retains its separately labeled legacy scan budget.
        var budget = hasOptimizedPath
            ? 64L * (sources + expected.Length + 1)
            : 4L * Nodes * sources;

        // WHY: Keep the native statement and plan even if the native-work reader rejects its diagnostics.
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + $"-ancestor-index-forest-{forest}-chain-{chain}-sources-{sources}-inclusive-{inclusive}",
            [
                $"Nodes={Nodes}; forest={forest}; chain={chain}; sources={sources}; inclusive={inclusive}; "
                + $"sourceBinding={hasBinding}; orderedJoin={hasOrderedJoin}; optimized={hasOptimizedPath}; "
                + $"budget={budget}; resultRows={actual.Length}; indexAttempts={
                    indexAttempts}; traversals={traversals}",
                sql,
                plan,
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        var work = Engine == "MySql"
            ? DensePathNativeWork.MySql(plan)
            : DensePathNativeWork.MariaDb(plan);

        // WHY: ICP can hide a full index scan behind a small post-filter row count in ANALYZE.
        var nativeWork = Math.Max(work.OperatorTuples, Math.Max(indexAttempts, traversals));

        // Assert
        _output.WriteLine(
            $"Forest={forest}; chain={chain}; sources={sources}; inclusive={inclusive}; "
            + $"binding={hasBinding}; orderedJoin={hasOrderedJoin}; optimized={hasOptimizedPath}; "
            + $"operatorTuples={work.OperatorTuples}; indexAttempts={
                indexAttempts}; traversals={traversals}; budget={budget}.");

        Assert.True(hasOptimizedPath);
        Assert.Equal(
            expected,
            actual
                .OrderBy(pair => pair.Source)
                .ThenBy(pair => pair.Owner));

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

            if (!forest
                && !chain)
            {
                Assert.Contains(ancestorIndex, plan, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Counts executed MySQL materialization rows, independently of legitimate streamed output size.</summary>
    private static long MaterializedTuples(
        string plan
    )
    {
        var matches = MaterializedTuplesRegex()
            .Matches(plan);

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                "The executed MySQL plan did not expose its materialization operators.");
        }

        return checked((long)matches.Sum(match =>
            double.Parse(match.Groups["rows"].Value, CultureInfo.InvariantCulture)
            * double.Parse(match.Groups["loops"].Value, CultureInfo.InvariantCulture)));
    }

    /// <summary>Keeps provider-bound GUID plan diagnostics valid on both native JSON writers.</summary>
    private static Guid PrintableGuid() => new(
        Encoding.ASCII.GetBytes(
            Guid
                .NewGuid()
                .ToString("N", CultureInfo.InvariantCulture)[..16]));

    [GeneratedRegex(
        @"(?im)^.*(?:materializ|temporary table).*\(actual time=[^)]*rows=(?<rows>[0-9.]+) loops=(?<loops>[0-9.]+)",
        RegexOptions.None,
        "en-DE")]
    private static partial Regex MaterializedTuplesRegex();

    /// <summary>Retains scalar identities and distance without materializing hierarchy payload.</summary>
    private sealed record Pair(
        int Source,
        int Owner,
        int Distance
    );
}
