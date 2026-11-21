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
    public async Task WherePathEdges_LongPaths_RequireEveryActualParent(
        string parentKind,
        string filter
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var nodes = LongChain(scope, tree, 1, 6);
        var foreignTree = LongChain(scope, Guid.NewGuid(), 101, 6);
        var foreignScope = LongChain(
            Guid.NewGuid(),
            tree,
            parentKind == "foreign-scope"
                ? 1
                : 201,
            6);

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

        var all = nodes
            .Concat(foreignTree)
            .Concat(foreignScope)
            .ToArray();

        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(
            scope,
            6,
            all,
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(scope);

        var rows = LongRows(context, scope, 1, 1, 6);
        var expected = ExpectedLongRows(
            all,
            scope,
            tree,
            1,
            1,
            6,
            filter);

        // Act
        var query = filter switch
        {
            "independent" => hierarchy.WherePathEdges(
                rows,
                row => row.Upper,
                row => row.Node,
                (row, parent, child) => child.EdgeOpen),
            "correlated" => hierarchy.WherePathEdges(
                rows,
                row => row.Upper,
                row => row.Node,
                (row, parent, child) => child.EdgeOpen
                    && child.FromDate <= row.Day
                    && (!child.ToDate.HasValue || row.Day <= child.ToDate.Value)),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(
                rows,
                row => row.Node,
                (row, owner) => owner.Candidate,
                (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var actual = await ReadLongRowsAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task WherePathEdges_LongPaths_RespectVisibilityOnlyBelowTheirUpperEndpoint(
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
        await using var context = await CreateLongPathContextAsync(
            scope,
            9,
            nodes,
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .InTree(tree);

        var rows = LongRows(context, scope, 4, 4, 9);
        var expected = ExpectedLongRows(
            nodes,
            scope,
            tree,
            4,
            4,
            9,
            filter);

        // Act
        var query = filter switch
        {
            "independent" => hierarchy.WherePathEdges(
                rows,
                row => row.Upper,
                row => row.Node,
                (row, parent, child) => child.EdgeOpen),
            "correlated" => hierarchy.WherePathEdges(
                rows,
                row => row.Upper,
                row => row.Node,
                (row, parent, child) => child.EdgeOpen
                    && child.FromDate <= row.Day
                    && (!child.ToDate.HasValue || row.Day <= child.ToDate.Value)),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(
                rows,
                row => row.Node,
                (row, owner) => owner.Candidate,
                (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var actual = await ReadLongRowsAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task WherePathEdges_LongPaths_IgnoreIncomingAndOffPathFailures(
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
            Scope = scope,
            TreeId = tree,
            Id = 9,
            ParentId = null,
            Left = 16,
            Right = 17,
            Depth = 1,
            Position = 1,
            EdgeOpen = false,
        };

        // WHY: Bounds still describe the valid chain plus its sibling. Only the unrelated incoming
        // Parent links are deliberately invalid; completeness below the chosen upper must ignore them.
        var all = nodes
            .Append(sibling)
            .ToArray();

        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(
            scope,
            8,
            all,
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .InTree(tree);

        var rows = LongRows(context, scope, 3, 3, 8);
        var expected = ExpectedLongRows(
            all,
            scope,
            tree,
            3,
            3,
            8,
            filter);

        // Act
        var query = filter switch
        {
            "independent" => hierarchy.WherePathEdges(
                rows,
                row => row.Upper,
                row => row.Node,
                (row, parent, child) => child.EdgeOpen),
            "correlated" => hierarchy.WherePathEdges(
                rows,
                row => row.Upper,
                row => row.Node,
                (row, parent, child) => child.EdgeOpen
                    && child.FromDate <= row.Day
                    && (!child.ToDate.HasValue || row.Day <= child.ToDate.Value)),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(
                rows,
                row => row.Node,
                (row, owner) => owner.Candidate,
                (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var actual = await ReadLongRowsAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, actual);
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>Checks one real statement and no hierarchy tracking or materialization after the scalar read.</summary>
    private static void AssertLongReadBoundary(
        DbContext context,
        CompositionCommandProbe probe
    )
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
    private readonly record struct LongPathOccurrence(
        int Origin,
        int Node,
        DateOnly Day
    );
}
