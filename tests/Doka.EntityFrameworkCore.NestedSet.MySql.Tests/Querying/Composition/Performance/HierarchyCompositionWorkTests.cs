using System.Globalization;
using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Checks executed MySQL work independently of machine-dependent elapsed time.</summary>
public sealed class HierarchyCompositionWorkTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    private const int Nodes = 100_000;
    private readonly ITestOutputHelper _output;

    /// <summary>Uses an isolated MySQL resource and retains native work observations.</summary>
    /// <param name="fixture">The provider-owned fixture; individual cases own their databases.</param>
    /// <param name="output">The sink retaining observed tuple and access-path work.</param>
    public HierarchyCompositionWorkTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _output = output;
    }

    /// <summary>
    /// Verifies the typed provider's pair semantics and required native-work contract.
    /// </summary>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task ConfiguredProviderPairsPreserveSemanticsAndQualifyAvailableNativeWork()
    {
        // Arrange
        await using var configured = new TreeContext(ModelCompatibilityDatabase.Options<TreeContext>(Engine));
        var family = configured.Database.IsMariaDb();
        var strategy = NestedSetProviderCapabilities
            .Resolve(configured)
            .ResolveSubtreeQueryStrategy(configured);

        await using var database = await TestDatabase.CreateAsync(Engine);
        await PreparePairsAsync(database);

        // Act
        var observations = await ObservePairProfilesAsync(database);

        // Assert
        // WHY: The installed typed adapter always exposes the configured family. The MySQL fixture
        // must qualify its required physical strategy instead of accepting a legacy semantic-only fallback.
        Assert.False(family);
        Assert.Equal(NestedSetSubtreeQueryStrategy.RelatedFirstUnion, strategy);
        Assert.All(observations, AssertObservation);
    }

    /// <summary>Prepares the same independent node and payload fixture for both capability contracts.</summary>
    private async Task PreparePairsAsync(
        TestDatabase database
    )
    {
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, Nodes);
        await SeedPayloadAsync(setup);
        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(setup, Engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<UnrelatedRow>(setup, Engine);
    }

    /// <summary>Executes every existing profile regardless of whether optimized work can be qualified.</summary>
    private async Task<List<WorkObservation>> ObservePairProfilesAsync(
        TestDatabase database
    )
    {
        var observations = new List<WorkObservation>();

        foreach (var anchors in new[] { 1, 1_000 })
        {
            foreach (var direct in new[] { false, true })
            {
                observations.Add(await ObservePairsAsync(database, anchors, 100, includeRoot: true, direct));
            }
        }

        observations.Add(await ObservePairsAsync(database, 1_000, Nodes, includeRoot: true, direct: false));
        observations.Add(await ObservePairsAsync(database, 1_000, Nodes, includeRoot: false, direct: false));

        return observations;
    }

    /// <summary>Distinguishes dense off-path failures, actual path rejection, and a valid full-length path.</summary>
    /// <param name="shape">The independent adjacency scenario; no database optimizer setting changes.</param>
    [Theory]
    [InlineData("DenseOffPath")]
    [InlineData("DenseOnPath")]
    [InlineData("CleanLongPath")]
    [Trait("Category", "Capacity")]
    public async Task DensePathEvidenceAvoidsRepeatedGlobalInvalidSetScans(
        string shape
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, Nodes, deep: true);
        await SeedPayloadAsync(setup);

        if (shape != "CleanLongPath")
        {
            await setup
                .Set<TreeNode>()
                .Where(node => node.NodeId <= Nodes / 2 && node.NodeId % 2 == 0)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.Payload, "blocked"),
                    CancellationToken.None);
        }

        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(setup, Engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<UnrelatedRow>(setup, Engine);
        var firstAnchor = shape == "DenseOffPath" ? Nodes - 999 : 1;
        var expected = shape == "DenseOnPath" ? [] : Expected(firstAnchor, 10, Nodes - 9, Nodes);
        var requiredEdges = shape == "DenseOnPath"
            ? 200L
            : Enumerable
                .Range(firstAnchor, 10)
                .Sum(anchor => Enumerable
                    .Range(Nodes - 9, 10)
                    .Sum(fact => (long)fact - anchor));

        var budget = CompositionPlanWorkBudget.ForPaths(Nodes, 100, requiredEdges);

        // Act
        var observation = await ObserveAsync(
            database,
            "path-" + shape,
            context => BuildPath(context, firstAnchor),
            expected,
            budget);

        // Assert
        AssertObservation(observation);
    }

    /// <summary>Observes a public subtree composition with a direct predicate or a physical payload join.</summary>
    private Task<WorkObservation> ObservePairsAsync(
        TestDatabase database,
        int anchors,
        int facts,
        bool includeRoot,
        bool direct
    )
    {
        var firstAnchor = Nodes - anchors + (includeRoot ? 2 : 1);
        var firstFact = Nodes - facts + 1;
        var expected = ExpectedPairs(firstAnchor, firstFact, includeRoot);
        var contributions = expected.Sum(row => row.Contributions);
        var budget = CompositionPlanWorkBudget.ForPairs(Nodes, anchors, facts, contributions);
        var profile = $"pairs-anchors-{anchors}-facts-{facts}-root-{includeRoot}-direct-{direct}";

        return ObserveAsync(
            database,
            profile,
            context => BuildPairs(context, firstAnchor, firstFact, includeRoot, direct),
            expected,
            budget);
    }

    /// <summary>Builds the actual library path/ancestor filters after the requested subtree source.</summary>
    private static IQueryable<CompositionCapacityTotal> BuildPairs(
        TreeContext context,
        int firstAnchor,
        int firstFact,
        bool includeRoot,
        bool direct
    )
    {
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var pairs = hierarchy.SelectSubtrees(
            anchor => (includeRoot && anchor.NodeId == 1) || anchor.NodeId >= firstAnchor,
            (anchor, node, distance) => new Contribution
            {
                Anchor = anchor,
                Node = node,
                FactKey = node.NodeId
            });

        var source = direct
            ? pairs.Where(row => row.Node.NodeId >= firstFact)
            : pairs.Join(
                context
                    .Set<UnrelatedRow>()
                    .Where(fact => fact.Id >= firstFact),
                row => row.Node.NodeId,
                fact => fact.Id,
                (row, fact) => new Contribution
                {
                    Anchor = row.Anchor,
                    Node = row.Node,
                    FactKey = fact.Id,
                });

        var paths = hierarchy.WherePathEdges(
            source,
            row => row.Anchor,
            row => row.Node,
            (_, parent, child) => child.Payload != "blocked");

        var any = hierarchy.WhereAnyAncestorOrSelf(
            paths,
            row => row.Node,
            (_, owner) => owner.Payload != "blocked");

        var nearest = hierarchy.WhereNearestAncestorOrSelf(
            any,
            row => row.Node,
            (_, owner) => owner.Depth >= 0,
            (_, owner) => owner.Payload != "blocked");

        return Totals(nearest);
    }

    /// <summary>Isolates path proof work using a bounded source with physically joined visible endpoints.</summary>
    private static IQueryable<CompositionCapacityTotal> BuildPath(
        TreeContext context,
        int firstAnchor
    )
    {
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var nodes = context
            .Set<TreeNode>()
            .Where(node => node.Tree == 1 && node.TreeId == Guid.Empty);

        var anchors = nodes.Where(node => node.NodeId >= firstAnchor && node.NodeId < firstAnchor + 10);
        var payload = context
            .Set<UnrelatedRow>()
            .Where(fact => fact.Id >= Nodes - 9);

        var source = anchors
            .Join(
                payload,
                _ => 1,
                _ => 1,
                (anchor, fact) => new
                {
                    Anchor = anchor,
                    Fact = fact,
                })
            .Join(
                nodes,
                row => row.Fact.Id,
                node => node.NodeId,
                (row, node) => new Contribution
                {
                    Anchor = row.Anchor,
                    Node = node,
                    FactKey = row.Fact.Id,
                });

        var paths = hierarchy.WherePathEdges(
            source,
            row => row.Anchor,
            row => row.Node,
            (_, parent, child) => child.Payload != "blocked");

        return Totals(paths);
    }

    /// <summary>Returns only scalar totals; entity materialization cannot hide the server-side work.</summary>
    private static IQueryable<CompositionCapacityTotal> Totals(
        IQueryable<Contribution> source
    ) => source
        .GroupBy(row => row.Anchor.NodeId)
        .OrderBy(group => group.Key)
        .Select(group => new CompositionCapacityTotal(
            group.Key,
            group.LongCount(),
            group.Sum(row => (long)row.FactKey)));

    /// <summary>Derives wide-tree totals from direct parent identity without consulting mapped boundaries.</summary>
    private static CompositionCapacityTotal[] ExpectedPairs(
        int firstAnchor,
        int firstFact,
        bool includeRoot
    )
    {
        var result = new List<CompositionCapacityTotal>();

        if (includeRoot)
        {
            var count = Nodes - firstFact + 1;
            result.Add(new CompositionCapacityTotal(1, count, ((long)firstFact + Nodes) * count / 2));
        }

        for (var key = Math.Max(firstAnchor, firstFact); key <= Nodes; key++)
        {
            result.Add(new CompositionCapacityTotal(key, 1, key));
        }

        return result.ToArray();
    }

    /// <summary>Derives the known chain's complete contribution bag without reading stored coordinates.</summary>
    private static CompositionCapacityTotal[] Expected(
        int firstAnchor,
        int anchors,
        int firstFact,
        int lastFact
    ) => Enumerable
        .Range(firstAnchor, anchors)
        .Select(anchor => new CompositionCapacityTotal(
            anchor,
            lastFact - firstFact + 1,
            ((long)firstFact + lastFact) * (lastFact - firstFact + 1) / 2))
        .ToArray();

    /// <summary>Retains the read-derived native plan before making deterministic work assertions.</summary>
    private async Task<WorkObservation> ObserveAsync(
        TestDatabase database,
        string profile,
        Func<TreeContext, IQueryable<CompositionCapacityTotal>> build,
        CompositionCapacityTotal[] expected,
        CompositionPlanWorkBudget budget
    )
    {
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        var commandProbe = new CompositionCommandProbe();
        await using var context = database.CreateContext(planProbe, commandProbe);
        var query = build(context);
        var constructionCommands = commandProbe.CommandCount;
        CompositionCapacityTotal[] actual;

        try
        {
            actual = await query.ToArrayAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                "MySql-composition-work-failed-" + profile,
                [error.ToString(), query.ToQueryString()]);

            throw;
        }

        var commands = commandProbe.CommandCount;
        var materialized = planProbe.MaterializedNodes;
        var tracked = context
            .ChangeTracker
            .Entries()
            .Count();

        var parameters = planProbe.ParameterBindings[0].Length;
        var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, 0, Engine);
        var work = CompositionPlanWork.ReadMySqlTree(plan);
        var fullPrefixLoops = CompositionPlanWork.FullTreePrefixLoops(plan);

        await QueryPlanTestSupport.WriteEvidenceAsync(
            "MySql-composition-work-" + profile,
            [
                $"Profile={profile}; maximumOperatorTuples={work.MaximumOperatorTuples}; "
                + $"maximumMaterializedTuples={work.MaximumMaterializedTuples}; "
                + $"fullTreePrefixLoops={fullPrefixLoops}; "
                + $"rootOutputTuples={work.RootOutputTuples}; actualResultRows={actual.Length}; "
                + $"operatorBudget={budget.OperatorTuples}; materializationBudget={budget.MaterializedTuples}",
                planProbe.Commands[0],
                plan,
            ]);

        return new WorkObservation(
            profile,
            actual,
            expected,
            work,
            budget,
            fullPrefixLoops,
            constructionCommands,
            commands,
            parameters,
            materialized,
            tracked);
    }

    /// <summary>Checks semantics, deferred scalar execution, and deterministic iterator work separately.</summary>
    private void AssertObservation(
        WorkObservation observation
    )
    {
        AssertSemanticsAndExecution(observation);
        Assert.InRange(observation.Work.MaximumOperatorTuples, 0, observation.Budget.OperatorTuples);
        Assert.InRange(observation.Work.MaximumMaterializedTuples, 0, observation.Budget.MaterializedTuples);
        Assert.InRange(observation.FullPrefixLoops * Nodes, 0, observation.Budget.OperatorTuples);
    }

    /// <summary>Checks the shared result and I/O contract while retaining all native work observations.</summary>
    private void AssertSemanticsAndExecution(
        WorkObservation observation
    )
    {
        _output.WriteLine(
            $"{observation.Profile}; "
            + $"maximumOperatorTuples={observation.Work.MaximumOperatorTuples}; "
            + $"maximumMaterializedTuples={observation.Work.MaximumMaterializedTuples}; "
            + $"fullTreePrefixLoops={observation.FullPrefixLoops}");

        Assert.Equal(observation.Expected, observation.Actual);
        Assert.Equal(0, observation.ConstructionCommands);
        Assert.Equal(1, observation.Commands);
        Assert.InRange(observation.Parameters, 0, 999);
        Assert.Equal(0, observation.Materialized);
        Assert.Equal(0, observation.Tracked);
        Assert.Equal((double)observation.Actual.Length, observation.Work.RootOutputTuples);
    }

    /// <summary>Copies the fixture's existing node keys into payload with no second tracked population.</summary>
    private static async Task SeedPayloadAsync(
        TreeContext context
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var source = context.Model.FindEntityType(typeof(TreeNode))!;
        var target = context.Model.FindEntityType(typeof(UnrelatedRow))!;
        var sourceStore = StoreObjectIdentifier.Table(source.GetTableName()!, source.GetSchema());
        var targetStore = StoreObjectIdentifier.Table(target.GetTableName()!, target.GetSchema());
        var sourceKey =
            sql.DelimitIdentifier(source.FindProperty(nameof(TreeNode.NodeId))!.GetColumnName(sourceStore)!);

        var targetKey =
            sql.DelimitIdentifier(target.FindProperty(nameof(UnrelatedRow.Id))!.GetColumnName(targetStore)!);

        var value = sql.DelimitIdentifier(target.FindProperty(nameof(UnrelatedRow.Value))!.GetColumnName(targetStore)!);

        var statement = $"INSERT INTO {sql.DelimitIdentifier(targetStore.Name, targetStore.Schema)} "
            + $"({targetKey}, {value}) SELECT {sourceKey}, 'fact' "
            + $"FROM {sql.DelimitIdentifier(sourceStore.Name, sourceStore.Schema)} "
            + $"WHERE {sourceKey} <= {Nodes.ToString(CultureInfo.InvariantCulture)}";

        // WHY: Only fixed fixture cardinality is interpolated; native mapping supplies every delimited identifier.
        await context.Database.ExecuteSqlRawAsync(statement, CancellationToken.None);
    }

    /// <summary>Keeps actual endpoint entities available to every public composition primitive.</summary>
    private sealed class Contribution
    {
        /// <summary>Gets the selected upper endpoint.</summary>
        public required TreeNode Anchor { get; init; }

        /// <summary>Gets the selected lower endpoint.</summary>
        public required TreeNode Node { get; init; }

        /// <summary>Gets the physically joined payload identity.</summary>
        public required int FactKey { get; init; }
    }

    /// <summary>Retains completed operation observations until the test's single assertion phase.</summary>
    private sealed record WorkObservation(
        string Profile,
        CompositionCapacityTotal[] Actual,
        CompositionCapacityTotal[] Expected,
        CompositionPlanWork Work,
        CompositionPlanWorkBudget Budget,
        double FullPrefixLoops,
        int ConstructionCommands,
        int Commands,
        int Parameters,
        int Materialized,
        int Tracked
    );
}
