namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Long paths require local direct parents and ignore foreign-scope identity decoys.</summary>
    /// <param name="parentKind">The stored intermediate link, or an equal-key foreign-scope decoy.</param>
    /// <param name="filter">The independent, row-correlated, or structural nearest path contract.</param>
    [Theory]
    [InlineData("valid", "independent")]
    [InlineData("missing", "independent")]
    [InlineData("skipped", "independent")]
    [InlineData("foreign-tree", "independent")]
    [InlineData("foreign-scope", "independent")]
    [InlineData("valid", "correlated")]
    [InlineData("missing", "correlated")]
    [InlineData("skipped", "correlated")]
    [InlineData("foreign-tree", "correlated")]
    [InlineData("foreign-scope", "correlated")]
    [InlineData("valid", "nearest")]
    [InlineData("missing", "nearest")]
    [InlineData("skipped", "nearest")]
    [InlineData("foreign-tree", "nearest")]
    [InlineData("foreign-scope", "nearest")]
    public async Task LongPathsRequireEveryActualParent(
        string parentKind,
        string filter
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var nodes = LongChain(scope, tree, 1, 6);
        var foreignTree = LongChain(scope, Guid.NewGuid(), 101, 6);
        var foreignScope = LongChain(Guid.NewGuid(), tree, parentKind == "foreign-scope" ? 1 : 201, 6);

        // WHY: The compound foreign key forbids a link to a key that exists only in another scope.
        // Keep that case reachable: its real local parent remains valid while the equal foreign key
        // has a visibility gap. A foreign scope's invalid-child set must never reject this local path.
        if (parentKind == "foreign-scope")
        {
            foreignScope[2].Visible = false;
            foreignScope[2].EdgeOpen = false;
            foreignScope[2].Candidate = true;
            foreignScope[2].Matches = false;
        }

        nodes[3].ParentId = parentKind switch
        {
            "valid" => 3,
            "missing" => null,
            "skipped" => 1,
            "foreign-tree" => 103,
            "foreign-scope" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(parentKind)),
        };

        var all = nodes.Concat(foreignTree).Concat(foreignScope).ToArray();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(scope, 6, all, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var rows = LongRows(context, scope, 1, 1, 6);
        var expected = ExpectedLongRows(all, scope, tree, 1, 1, 6, filter);

        // Act
        var query = filter switch
        {
            "independent" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => child.EdgeOpen),
            "correlated" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => child.EdgeOpen && child.FromDate <= row.Day
                    && (!child.ToDate.HasValue || row.Day <= child.ToDate.Value)),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var actual = await ReadLongRowsAsync(query);

        // Assert
        Assert.Equal(expected, actual);
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>Long-path completeness detects visibility gaps without inspecting above the chosen owner.</summary>
    /// <param name="visibility">The absent intermediate nodes, or nodes hidden above the selected owner.</param>
    /// <param name="filter">The independent, row-correlated, or structural nearest path contract.</param>
    [Theory]
    [InlineData("visible", "independent")]
    [InlineData("one-gap", "independent")]
    [InlineData("two-gaps", "independent")]
    [InlineData("above-owner", "independent")]
    [InlineData("visible", "correlated")]
    [InlineData("one-gap", "correlated")]
    [InlineData("two-gaps", "correlated")]
    [InlineData("above-owner", "correlated")]
    [InlineData("visible", "nearest")]
    [InlineData("one-gap", "nearest")]
    [InlineData("two-gaps", "nearest")]
    [InlineData("above-owner", "nearest")]
    public async Task LongPathsRespectVisibilityOnlyBelowTheirUpperEndpoint(
        string visibility,
        string filter
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var nodes = LongChain(scope, tree, 1, 9);

        foreach (var node in nodes)
        {
            node.Candidate = node.Id == 4;
            node.Visible = visibility switch
            {
                "visible" => true,
                "one-gap" => node.Id != 6,
                "two-gaps" => node.Id is not (6 or 7),
                "above-owner" => node.Id is not (2 or 3),
                _ => throw new ArgumentOutOfRangeException(nameof(visibility)),
            };
        }

        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(scope, 9, nodes, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(tree);
        var rows = LongRows(context, scope, 4, 4, 9);
        var expected = ExpectedLongRows(nodes, scope, tree, 4, 4, 9, filter);

        // Act
        var query = filter switch
        {
            "independent" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => child.EdgeOpen),
            "correlated" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => child.EdgeOpen && child.FromDate <= row.Day
                    && (!child.ToDate.HasValue || row.Day <= child.ToDate.Value)),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var actual = await ReadLongRowsAsync(query);

        // Assert
        Assert.Equal(expected, actual);
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>An invalid incoming upper edge and an invalid sibling cannot reject a valid long suffix.</summary>
    /// <param name="filter">The independent, row-correlated, or structural nearest path contract.</param>
    [Theory]
    [InlineData("independent")]
    [InlineData("correlated")]
    [InlineData("nearest")]
    public async Task LongPathsIgnoreIncomingAndOffPathFailures(
        string filter
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var nodes = LongChain(scope, tree, 1, 8);
        nodes[0].Right = 18;
        nodes[2].ParentId = null;
        nodes[2].EdgeOpen = false;

        foreach (var node in nodes)
        {
            node.Candidate = node.Id == 3;
        }

        var sibling = new CompositionNode
        {
            Scope = scope, TreeId = tree, Id = 9, ParentId = null,
            Left = 16, Right = 17, Depth = 1, Position = 1, EdgeOpen = false,
        };

        // WHY: Bounds still describe the valid chain plus its sibling. Only the unrelated incoming
        // Parent links are deliberately invalid; completeness below the chosen upper must ignore them.
        var all = nodes.Append(sibling).ToArray();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(scope, 8, all, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(tree);
        var rows = LongRows(context, scope, 3, 3, 8);
        var expected = ExpectedLongRows(all, scope, tree, 3, 3, 8, filter);

        // Act
        var query = filter switch
        {
            "independent" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => child.EdgeOpen),
            "correlated" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => child.EdgeOpen && child.FromDate <= row.Day
                    && (!child.ToDate.HasValue || row.Day <= child.ToDate.Value)),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var actual = await ReadLongRowsAsync(query);

        // Assert
        Assert.Equal(expected, actual);
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>Long paths preserve origin/date bags while excluding endpoints outside the bound tree.</summary>
    /// <param name="filter">Whether edge criteria are independent, per-date, or structural nearest.</param>
    [Theory]
    [InlineData("independent")]
    [InlineData("correlated")]
    [InlineData("nearest")]
    public async Task LongPathsKeepOriginBagsAndCompleteBoundIdentities(
        string filter
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var otherScope = Guid.NewGuid();
        var nodes = LongChain(scope, tree, 1, 6);
        nodes[2].ToDate = new DateOnly(2044, 10, 19);
        var foreignTree = LongChain(scope, Guid.NewGuid(), 101, 6);
        var foreignScope = LongChain(otherScope, tree, 1, 6);
        foreignTree[3].ParentId = null;
        foreignScope[3].ParentId = null;
        var all = nodes.Concat(foreignTree).Concat(foreignScope).ToArray();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(scope, 6, all, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(tree);
        var localRows = LongRows(context, scope, 1, 2, 6);
        var anchors = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id <= 2);
        var foreign = context.Set<CompositionNode>().Where(node =>
            (node.Scope == scope && node.Id == 106) || (node.Scope == otherScope && node.Id == 6));

        var foreignRows = anchors.Join(foreign, _ => 1, _ => 1, (upper, node) => new { Upper = upper, Node = node })
            .Join(context.Set<CompositionInputOne>().Where(fact => fact.Scope == scope), _ => 1, _ => 1,
                (pair, fact) => new LongPathRow { Upper = pair.Upper, Node = pair.Node, Day = fact.Date });

        var source = localRows.Concat(foreignRows);
        var rows = source.Concat(source);
        var expected = ExpectedLongRows(all, scope, tree, 1, 2, 6, filter)
            .SelectMany(row => new[] { row, row }).OrderBy(row => row.Origin).ThenBy(row => row.Day).ToArray();

        // Act
        var query = filter switch
        {
            "independent" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => child.EdgeOpen),
            "correlated" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => child.EdgeOpen && child.FromDate <= row.Day
                    && (!child.ToDate.HasValue || row.Day <= child.ToDate.Value)),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var actual = await ReadLongRowsAsync(query);

        // Assert
        Assert.Equal(expected, actual);
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>Creates canonical chain coordinates while keeping the oracle's Parent links explicit.</summary>
    private static CompositionNode[] LongChain(Guid scope, Guid tree, int first, int count)
        => Enumerable.Range(0, count).Select(depth => new CompositionNode
        {
            Scope = scope, TreeId = tree, Id = first + depth,
            ParentId = depth == 0 ? null : first + depth - 1,
            Left = depth + 1, Right = (2L * count) - depth, Depth = depth,
            Candidate = depth == 0, Matches = true,
        }).ToArray();

    /// <summary>Seeds only small independent scopes and observes the actual scalar query boundary.</summary>
    private async Task<CompositionContext> CreateLongPathContextAsync(
        Guid scope,
        int lower,
        CompositionNode[] nodes,
        CompositionCommandProbe probe
    )
    {
        var context = await _fixture.CreateContextAsync<CompositionContext>(Engine,
            static options => new CompositionContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, probe.RecordMaterialization);

        try
        {
            await context.AddRangeAsync(nodes, CancellationToken.None);
            await context.AddRangeAsync(LongDays().Select(day => new CompositionInputOne
            {
                Scope = scope, NodeId = lower, Date = day, Amount = 1,
            }), CancellationToken.None);

            await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Joins persisted payload rows to selected visible endpoints before either path strategy.</summary>
    private static IQueryable<LongPathRow> LongRows(
        CompositionContext context,
        Guid scope,
        int firstUpper,
        int lastUpper,
        int lower
    ) => context.NestedSet<CompositionNode>().ForScope(scope)
        .SelectSubtrees(node => node.Id >= firstUpper && node.Id <= lastUpper,
            (upper, node, distance) => new { Upper = upper, Node = node })
        .Where(row => row.Node.Id == lower)
        .Join(context.Set<CompositionInputOne>().Where(fact => fact.Scope == scope && fact.NodeId == lower),
            row => row.Node.Id, fact => fact.NodeId,
            (pair, fact) => new LongPathRow { Upper = pair.Upper, Node = pair.Node, Day = fact.Date });

    /// <summary>Reads a scalar bag without materializing the hierarchy aliases carried by the source.</summary>
    private static Task<LongPathOccurrence[]> ReadLongRowsAsync(IQueryable<LongPathRow> rows)
        => rows.OrderBy(row => row.Upper.Id).ThenBy(row => row.Day)
            .Select(row => new LongPathOccurrence(row.Upper.Id, row.Node.Id, row.Day))
            .ToArrayAsync(CancellationToken.None);

    /// <summary>Walks explicit Parent identities rather than counting or excluding coordinate intervals.</summary>
    private static LongPathOccurrence[] ExpectedLongRows(
        CompositionNode[] nodes,
        Guid scope,
        Guid tree,
        int firstUpper,
        int lastUpper,
        int lower,
        string filter
    )
    {
        var identities = nodes.ToDictionary(node => (node.Scope, node.Id));
        var result = new List<LongPathOccurrence>();

        foreach (var upper in Enumerable.Range(firstUpper, lastUpper - firstUpper + 1))
        {
            foreach (var day in LongDays())
            {
                var selectedUpper = filter == "nearest"
                    ? nodes.Single(node => node.Scope == scope && node.TreeId == tree && node.Candidate).Id
                    : upper;

                var current = identities[(scope, lower)];
                var accepted = current.Visible;
                var remaining = nodes.Length;

                while (accepted && current.Id != selectedUpper && remaining-- > 0)
                {
                    if (current.ParentId is not { } parentId
                        || !identities.TryGetValue((scope, parentId), out var parent)
                        || !parent.Visible || parent.TreeId != tree || current.TreeId != tree
                        || parent.Depth + 1 != current.Depth)
                    {
                        accepted = false;
                        break;
                    }

                    if (filter != "nearest" && (!current.EdgeOpen || (filter == "correlated"
                        && (day < current.FromDate || (current.ToDate.HasValue && day > current.ToDate.Value)))))
                    {
                        accepted = false;
                        break;
                    }

                    current = parent;
                }

                if (accepted && current.Id == selectedUpper && current.Visible && current.TreeId == tree)
                {
                    result.Add(new LongPathOccurrence(upper, lower, day));
                }
            }
        }

        return result.OrderBy(row => row.Origin).ThenBy(row => row.Day).ToArray();
    }

    /// <summary>Supplies two dates that differ across the explicit per-row edge validity boundary.</summary>
    private static DateOnly[] LongDays() => [new(2044, 10, 18), new(2044, 10, 20)];

    /// <summary>Checks one real statement and no hierarchy tracking or materialization after the scalar read.</summary>
    private static void AssertLongReadBoundary(DbContext context, CompositionCommandProbe probe)
    {
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Carries actual mapped endpoint aliases and one persisted payload date.</summary>
    private sealed class LongPathRow
    {
        /// <summary>Gets the selected visible upper endpoint.</summary>
        internal required CompositionNode Upper { get; init; }

        /// <summary>Gets the selected lower endpoint.</summary>
        internal required CompositionNode Node { get; init; }

        /// <summary>Gets the incoming payload row's independent edge-evaluation date.</summary>
        internal DateOnly Day { get; init; }
    }

    /// <summary>Retains the origin and date grain without retaining tracked hierarchy instances.</summary>
    /// <param name="Origin">The original upper-endpoint identity, even when nearest selects another owner.</param>
    /// <param name="Node">The accepted lower identity.</param>
    /// <param name="Day">The original payload occurrence's date.</param>
    private readonly record struct LongPathOccurrence(int Origin, int Node, DateOnly Day);
}
