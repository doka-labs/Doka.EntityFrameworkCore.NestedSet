namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Dense disjoint failures never cross path, endpoint, occurrence, scope, or tree boundaries.</summary>
    /// <param name="topology">The local first-edge or intermediate-node adversary.</param>
    /// <param name="filter">The independent, row-correlated, or structural-nearest path contract.</param>
    /// <param name="expectedCount">The independently enumerated positive occurrence count.</param>
    [Theory]
    [InlineData("off-path-only", "independent", 12)]
    [InlineData("off-path-only", "correlated", 10)]
    [InlineData("off-path-only", "nearest", 12)]
    [InlineData("first-child-missing", "independent", 8)]
    [InlineData("first-child-missing", "correlated", 6)]
    [InlineData("first-child-missing", "nearest", 4)]
    [InlineData("first-child-closed", "independent", 8)]
    [InlineData("first-child-closed", "correlated", 6)]
    [InlineData("first-child-closed", "nearest", 12)]
    [InlineData("hidden-intermediate", "independent", 4)]
    [InlineData("hidden-intermediate", "correlated", 2)]
    [InlineData("hidden-intermediate", "nearest", 4)]
    public async Task DenseDisjointFailuresPreserveOnlyTheirOwnParentWalks(
        string topology,
        string filter,
        int expectedCount
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var local = DensePathNodes(scope, treeId, topology);
        var all = local.Concat(DensePathDecoys(local, Guid.NewGuid(), treeId, 0))
            .Concat(DensePathDecoys(local, scope, Guid.NewGuid(), 5_000)).ToArray();

        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(scope, 7, all, probe);
        await context.AddRangeAsync(LongDays().Select(day => new CompositionInputOne
        {
            Scope = scope,
            NodeId = 13,
            Date = day,
            Amount = 1,
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(treeId);
        var firstBranch = LongRows(context, scope, 2, 3, 7);
        var secondBranch = LongRows(context, scope, 2, 2, 13);
        var originalRows = firstBranch.Concat(secondBranch);
        var rows = originalRows.Concat(originalRows);
        var expected = ExpectedLongRows(all, scope, treeId, 2, 3, 7, filter)
            .Concat(ExpectedLongRows(all, scope, treeId, 2, 2, 13, filter))
            .SelectMany(row => new[] { row, row })
            .OrderBy(row => row.Origin).ThenBy(row => row.Node).ThenBy(row => row.Day).ToArray();

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

        var actualRows = await ReadLongRowsAsync(query);
        var actual = actualRows.OrderBy(row => row.Origin).ThenBy(row => row.Node).ThenBy(row => row.Day).ToArray();

        // Assert
        Assert.Equal(expectedCount, expected.Length);
        Assert.Equal(expected, actual);
        Assert.All(actual.GroupBy(row => row), occurrences => Assert.Equal(2, occurrences.Count()));
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>Builds canonical sibling coordinates before applying explicit incoming-parent adversaries.</summary>
    private static CompositionNode[] DensePathNodes(Guid scope, Guid treeId, string topology)
    {
        var nodes = new List<CompositionNode>();
        Add(1, null);
        Add(2, 1);

        for (var id = 3; id <= 7; id++)
        {
            Add(id, id == 3 ? 2 : id - 1);
        }

        for (var id = 8; id <= 13; id++)
        {
            Add(id, id == 8 ? 2 : id - 1);
        }

        for (var branch = 0; branch < 32; branch++)
        {
            var first = 100 + (branch * 3);
            Add(first, 2);
            Add(first + 1, first);
            Add(first + 2, first + 1);
        }

        var children = nodes.ToLookup(node => node.ParentId);
        var next = 1L;
        Visit(nodes[0], 0, 0);
        var indexed = nodes.ToDictionary(node => node.Id);
        indexed[2].ParentId = null;
        indexed[2].EdgeOpen = false;
        indexed[2].Candidate = true;
        indexed[8].ToDate = new DateOnly(2044, 10, 19);

        // WHY: Every unrelated sibling branch contributes invalid children, including a visible child below
        // a hidden parent. None lies on either requested path, even though all share its scope and tree.
        for (var branch = 0; branch < 32; branch++)
        {
            var first = 100 + (branch * 3);
            indexed[first].ParentId = null;
            indexed[first].EdgeOpen = false;
            indexed[first + 1].Visible = false;
            indexed[first + 2].EdgeOpen = false;
        }

        switch (topology)
        {
            case "off-path-only":
                break;
            case "first-child-missing":
                indexed[3].ParentId = null;
                break;
            case "first-child-closed":
                indexed[3].EdgeOpen = false;
                break;
            case "hidden-intermediate":
                indexed[5].Visible = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(topology));
        }

        return nodes.ToArray();

        void Add(int id, int? parent)
        {
            nodes.Add(new CompositionNode
            {
                Scope = scope,
                TreeId = treeId,
                Id = id,
                ParentId = parent,
                Matches = true,
            });
        }

        void Visit(CompositionNode node, int depth, long position)
        {
            node.Left = next++;
            node.Depth = depth;
            node.Position = position;
            var childPosition = 0L;

            foreach (var child in children[node.Id])
            {
                Visit(child, depth + 1, childPosition++);
            }

            node.Right = next++;
        }
    }

    /// <summary>Copies overlapping coordinates into foreign identities with explicitly failing path criteria.</summary>
    private static IEnumerable<CompositionNode> DensePathDecoys(
        CompositionNode[] source,
        Guid scope,
        Guid treeId,
        int keyOffset
    ) => source.Select(node => new CompositionNode
    {
        Scope = scope,
        TreeId = treeId,
        Id = node.Id + keyOffset,
        ParentId = node.Id is 3 or 8 ? null : node.ParentId + keyOffset,
        Left = node.Left,
        Right = node.Right,
        Depth = node.Depth,
        Position = node.Position,
        Visible = node.Visible,
        EdgeOpen = false,
        Candidate = node.Candidate,
        Matches = false,
    });
}
