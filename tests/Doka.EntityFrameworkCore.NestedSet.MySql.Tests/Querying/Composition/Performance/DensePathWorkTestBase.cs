namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Qualifies dense failure paths with independent adjacency and native work instead of wall time.</summary>
public abstract partial class DensePathWorkTestBase : ProviderTest
{
    private readonly ITestOutputHelper _output;

    /// <summary>Uses the concrete engine owner and retains observed native work.</summary>
    /// <param name="fixture">The isolated relational resource bound to one concrete engine.</param>
    /// <param name="output">The sink retaining actual tuple and materialization observations.</param>
    protected DensePathWorkTestBase(
        IProviderFixture<RelationalFixture> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _output = output;
    }

    /// <summary>Retains original chain controls and rejecting upper, lower and same-block fringes.</summary>
    /// <param name="shape">The independent failure placement, without any SQL bucket calculation.</param>
    /// <param name="nodes">The persisted population; the three original controls also run at one million.</param>
    [Theory]
    [InlineData("DenseOffPath", 100_000)]
    [InlineData("DenseOnPath", 100_000)]
    [InlineData("CleanLongPath", 100_000)]
    [InlineData("DenseCandidatesAccept", 100_000)]
    [InlineData("DenseCandidatesReject", 100_000)]
    [InlineData("DenseCandidatesPriorAccept", 100_000)]
    [InlineData("DenseCandidatesPriorReject", 100_000)]
    [InlineData("SingleUpperFringe", 100_000)]
    [InlineData("SingleLowerFringe", 100_000)]
    [InlineData("SameBlockLower", 100_000)]
    [InlineData("UpperIncomingOnly", 100_000)]
    [InlineData("DenseOffPath", 1_000_000)]
    [InlineData("DenseOnPath", 1_000_000)]
    [InlineData("CleanLongPath", 1_000_000)]
    [Trait("Category", "Capacity")]
    public async Task DenseChainsKeepFailureEvidenceAndFringesBounded(
        string shape,
        int nodes
    )
    {
        // Arrange
        var scenario = DensePathWorkScenario.Chain(shape, nodes);
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await DensePathWorkFixture.SeedAsync(setup, scenario, Engine);
        var expected = scenario.Expected();
        var required = shape == "DenseOnPath" ? 200 : expected.RequiredEdges;
        var budget = CompositionPlanWorkBudget.ForPaths(nodes, 100, required);

        // Act
        var observation = await ObserveAsync(database, scenario, expected.Rows, budget);

        // Assert
        AssertObservation(observation);
    }

    /// <summary>Retains prior-block candidate identity across two overlapping authorized chains.</summary>
    /// <param name="shape">Whether the nearest prior candidate must pass or reject the value predicate.</param>
    [Theory]
    [InlineData("DenseCandidatesPriorAccept")]
    [InlineData("DenseCandidatesPriorReject")]
    [Trait("Category", "Capacity")]
    public async Task DensePriorCandidatesStayWithinTheirForestIdentity(
        string shape
    )
    {
        // Arrange
        var scenario = DensePathWorkScenario.Chain(shape, 100_000) with { Forest = true };
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await DensePathWorkFixture.SeedAsync(setup, scenario, Engine);
        var expected = scenario.Expected();
        var budget = CompositionPlanWorkBudget.ForPaths(
            scenario.StoredNodes,
            scenario
                .Pairs()
                .Count(),
            expected.RequiredEdges);

        // Act
        var observation = await ObserveAsync(database, scenario, expected.Rows, budget);

        // Assert
        AssertObservation(observation);
        Assert.Equal(shape == "DenseCandidatesPriorAccept" ? 20 : 0, observation.Actual.Length);
    }

    /// <summary>Separates sibling failures across distinct lower endpoints and overlapping forests.</summary>
    /// <param name="shape">The independent off-branch, selected-branch failure, or clean population.</param>
    /// <param name="forest">Whether the public query selects the complete authorized two-tree scope.</param>
    [Theory]
    [InlineData("DenseOffBranch", false)]
    [InlineData("DenseOffBranch", true)]
    [InlineData("SelectedBranchFailure", false)]
    [InlineData("SelectedBranchFailure", true)]
    [InlineData("CleanDistinctLower", false)]
    [InlineData("CleanDistinctLower", true)]
    [InlineData("RootAboveSiblingFailures", false)]
    [InlineData("RootAboveSiblingFailures", true)]
    [Trait("Category", "Capacity")]
    public async Task DisjointPathsKeepScopeTreeAndFailureWorkIndependent(
        string shape,
        bool forest
    )
    {
        // Arrange
        var scenario = DensePathWorkScenario.Disjoint(shape, forest);
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await DensePathWorkFixture.SeedAsync(setup, scenario, Engine);
        var expected = scenario.Expected();
        var budget = CompositionPlanWorkBudget.ForPaths(
            scenario.StoredNodes,
            scenario
                .Pairs()
                .Count(),
            expected.RequiredEdges);

        // Act
        var observation = await ObserveAsync(database, scenario, expected.Rows, budget);

        // Assert
        AssertObservation(observation);
        Assert.Equal(shape == "SelectedBranchFailure" ? 90L : 100L, observation.Actual.Sum(row => row.Contributions));
    }

    /// <summary>Builds scalar actual public path and nearest-owner composition over physical endpoint rows.</summary>
    private static IQueryable<CompositionCapacityTotal> Build(
        TreeContext context,
        DensePathWorkScenario scenario
    )
    {
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var nodes = context
            .Set<TreeNode>()
            .Where(node => node.Tree == 1 && (scenario.Forest || node.TreeId == Guid.Empty));

        IQueryable<Contribution> source;

        if (scenario.Branches)
        {
            var measured = context
                .Set<UnrelatedRow>()
                .Join(
                    nodes,
                    fact => fact.Id,
                    node => node.NodeId,
                    (fact, node) => new
                    {
                        Fact = fact,
                        Node = node,
                    });

            source = scenario.Name == "RootAboveSiblingFailures"
                ? measured.Join(
                    nodes.Where(node => node.Parent == null),
                    row => row.Node.TreeId,
                    anchor => anchor.TreeId,
                    (row, anchor) => new Contribution
                    {
                        Anchor = anchor,
                        Node = row.Node,
                        FactKey = row.Fact.Id,
                    })
                : measured.Join(
                    nodes,
                    row => row.Node.NodeId - row.Node.Depth + 501,
                    anchor => anchor.NodeId,
                    (row, anchor) => new Contribution
                    {
                        Anchor = anchor,
                        Node = row.Node,
                        FactKey = row.Fact.Id,
                    });
        }
        else if (scenario.Forest)
        {
            var anchors = nodes.Where(node => node.Depth >= scenario.FirstAnchor - 1
                && node.Depth < scenario.FirstAnchor + 9);

            var measured = context
                .Set<UnrelatedRow>()
                .Join(
                    nodes,
                    fact => fact.Id,
                    node => node.NodeId,
                    (fact, node) => new
                    {
                        Fact = fact,
                        Node = node,
                    })
                .Where(row => row.Node.Depth >= scenario.Nodes - 10);

            // WHY: Equal coordinates identify different physical nodes in each tree. Join source occurrences
            // by their explicit TreeId before applying the public forest operators, never as a cross product.
            source = anchors.Join(
                measured,
                anchor => anchor.TreeId,
                row => row.Node.TreeId,
                (anchor, row) => new Contribution
                {
                    Anchor = anchor,
                    Node = row.Node,
                    FactKey = row.Fact.Id,
                });
        }
        else
        {
            var anchors = nodes.Where(node => node.NodeId >= scenario.FirstAnchor
                && node.NodeId < scenario.FirstAnchor + 10);

            var payload = context
                .Set<UnrelatedRow>()
                .Where(fact => fact.Id >= scenario.Nodes - 9);

            source = anchors
                .Join(
                    payload,
                    _ => 1,
                    _ => 1,
                    (anchor, fact) => new
                    {
                        Anchor = anchor,
                        Fact = fact
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
        }

        var paths = scenario.Forest
            ? hierarchy.WherePathEdges(
                source,
                row => row.Anchor,
                row => row.Node,
                (_, parent, child) => child.Payload != "blocked")
            : hierarchy
                .InTree(Guid.Empty)
                .WherePathEdges(
                    source,
                    row => row.Anchor,
                    row => row.Node,
                    (_, parent, child) => child.Payload != "blocked");

        if (scenario.Branches
            && scenario.Name != "RootAboveSiblingFailures")
        {
            // WHY: The marked upper endpoint is the nearest eligible structural owner in its branch.
            // Overlapping coordinates in the other tree and scope must never supply its path or owner.
            paths = scenario.Forest
                ? hierarchy.WhereNearestAncestorOrSelf(
                    paths,
                    row => row.Node,
                    (_, owner) => owner.Payload == "upper",
                    (row, owner) => owner.NodeId == row.Anchor.NodeId)
                : hierarchy
                    .InTree(Guid.Empty)
                    .WhereNearestAncestorOrSelf(
                        paths,
                        row => row.Node,
                        (_, owner) => owner.Payload == "upper",
                        (row, owner) => owner.NodeId == row.Anchor.NodeId);
        }

        if (scenario.CandidateCutoff is { } cutoff)
        {
            // WHY: Prior profiles leave every lower endpoint's own range empty, exercising the search over
            // earlier dense candidate ranges. Rejection must not fall back from the nearest candidate to root.
            if (scenario.Forest)
            {
                paths = scenario.Name == "DenseCandidatesPriorAccept"
                    ? hierarchy.WhereNearestAncestorOrSelf(
                        paths,
                        row => row.Node,
                        (_, owner) => owner.Depth < cutoff,
                        (_, owner) => owner.Depth == cutoff - 1)
                    : hierarchy.WhereNearestAncestorOrSelf(
                        paths,
                        row => row.Node,
                        (_, owner) => owner.Depth < cutoff,
                        (_, owner) => owner.Depth == 0);
            }
            else
            {
                paths = scenario.Name is "DenseCandidatesAccept" or "DenseCandidatesPriorAccept"
                    ? hierarchy
                        .InTree(Guid.Empty)
                        .WhereNearestAncestorOrSelf(
                            paths,
                            row => row.Node,
                            (_, owner) => owner.NodeId <= cutoff,
                            (_, owner) => owner.NodeId == cutoff)
                    : hierarchy
                        .InTree(Guid.Empty)
                        .WhereNearestAncestorOrSelf(
                            paths,
                            row => row.Node,
                            (_, owner) => owner.NodeId <= cutoff,
                            (_, owner) => owner.NodeId == 1);
            }
        }

        return paths
            .GroupBy(row => row.Anchor.NodeId)
            .OrderBy(group => group.Key)
            .Select(group => new CompositionCapacityTotal(
                group.Key,
                group.LongCount(),
                group.Sum(row => (long)row.FactKey)));
    }

    /// <summary>Keeps real endpoint aliases available until every public composition predicate is translated.</summary>
    private sealed class Contribution
    {
        /// <summary>Gets the selected upper endpoint.</summary>
        public required TreeNode Anchor { get; init; }

        /// <summary>Gets the selected lower endpoint.</summary>
        public required TreeNode Node { get; init; }

        /// <summary>Gets the joined payload key used by the scalar aggregate.</summary>
        public required int FactKey { get; init; }
    }
}
