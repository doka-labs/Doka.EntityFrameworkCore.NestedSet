namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Path acceptance follows adjacency across compact, boundary, and empty evidence intervals.</summary>
    /// <param name="layout">The fixed canonical coordinate layout and local path adversary.</param>
    /// <param name="filter">The independent, row-correlated, or structural-nearest path contract.</param>
    /// <param name="expectedCount">The independently known accepted date-occurrence count.</param>
    [Theory]
    [InlineData("compact", "independent", 2)]
    [InlineData("compact", "nearest", 2)]
    [InlineData("compact-lower-failure", "independent", 0)]
    [InlineData("compact-lower-failure", "nearest", 0)]
    [InlineData("adjacent", "independent", 2)]
    [InlineData("adjacent", "nearest", 2)]
    [InlineData("adjacent-first-child-failure", "independent", 0)]
    [InlineData("adjacent-first-child-failure", "nearest", 0)]
    [InlineData("lower-boundary", "independent", 2)]
    [InlineData("lower-boundary", "nearest", 2)]
    [InlineData("lower-boundary-failure", "independent", 0)]
    [InlineData("lower-boundary-failure", "nearest", 0)]
    [InlineData("upper-boundary", "independent", 2)]
    [InlineData("upper-boundary", "nearest", 2)]
    [InlineData("interior", "independent", 2)]
    [InlineData("interior", "nearest", 2)]
    [InlineData("interior-failure", "independent", 0)]
    [InlineData("interior-failure", "nearest", 0)]
    [InlineData("empty-interior", "independent", 2)]
    [InlineData("empty-interior", "nearest", 2)]
    [InlineData("hidden-intermediate", "independent", 0)]
    [InlineData("hidden-intermediate", "nearest", 0)]
    [InlineData("compact", "correlated", 1)]
    [InlineData("adjacent", "correlated", 1)]
    [InlineData("empty-interior", "correlated", 1)]
    public async Task DensePathBoundariesFollowActualParentIdentity(
        string layout,
        string filter,
        int expectedCount
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var (local, lower) = DenseBoundaryNodes(scope, treeId, layout);
        var all = local.Concat(DensePathDecoys(local, Guid.NewGuid(), treeId, 0))
            .Concat(DensePathDecoys(local, scope, Guid.NewGuid(), 10_000)).ToArray();

        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(scope, lower, all, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(treeId);
        var rows = LongRows(context, scope, 2, 2, lower);
        var expected = ExpectedLongRows(all, scope, treeId, 2, 2, lower, filter);
        var expectedCoordinates = DenseBoundaryCoordinates(layout);

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
        Assert.Equal(expectedCoordinates, (local.Single(node => node.Id == 2).Left,
            local.Single(node => node.Id == lower).Left));

        Assert.Equal((1L, 400L), (local[0].Left, local[0].Right));
        Assert.Equal(expectedCount, expected.Length);
        Assert.Equal(expected, actual);
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>Creates dense valid coordinates before introducing independent Parent or visibility failures.</summary>
    private static (CompositionNode[] Nodes, int Lower) DenseBoundaryNodes(Guid scope, Guid treeId, string layout)
    {
        var leadingLeaves = layout switch
        {
            "adjacent" or "adjacent-first-child-failure" or "upper-boundary" => 9,
            "lower-boundary" or "lower-boundary-failure" => 8,
            _ => 0,
        };

        var interior = layout is "interior" or "interior-failure" or "empty-interior" or "hidden-intermediate";
        var lower = layout is "lower-boundary" or "lower-boundary-failure" ? 5 : 7;
        var nodes = new List<CompositionNode>();
        Add(1, null);
        var upperParent = 1;

        if (layout == "upper-boundary")
        {
            Add(10, 1);
            upperParent = 10;
        }

        for (var index = 0; index < leadingLeaves; index++)
        {
            Add(1_000 + index, upperParent);
        }

        Add(2, upperParent);
        Add(3, 2);

        if (interior)
        {
            for (var index = 0; index < 50; index++)
            {
                Add(2_000 + index, 3);
            }
        }

        for (var id = 4; id <= 7; id++)
        {
            Add(id, id - 1);
        }

        Add(8, 2);

        while (nodes.Count < 200)
        {
            Add(3_000 + nodes.Count, 8);
        }

        var children = nodes.ToLookup(node => node.ParentId);
        var next = 1L;
        Visit(nodes[0], 0, 0);
        var indexed = nodes.ToDictionary(node => node.Id);
        indexed[2].Candidate = true;
        indexed[4].ToDate = new DateOnly(2044, 10, 19);

        if (layout != "empty-interior")
        {
            // WHY: The upper's incoming failure is excluded. The following sibling has a large Right
            // but begins beyond the lower; neither is an actual edge in the requested parent walk.
            indexed[2].ParentId = null;
            indexed[2].EdgeOpen = false;
            indexed[8].ParentId = null;
            indexed[8].EdgeOpen = false;

            if (interior)
            {
                foreach (var id in Enumerable.Range(2_000, 50))
                {
                    indexed[id].ParentId = null;
                    indexed[id].EdgeOpen = false;
                }
            }
        }

        switch (layout)
        {
            case "compact-lower-failure":
            case "lower-boundary-failure":
                indexed[lower].ParentId = null;
                break;
            case "adjacent-first-child-failure":
                indexed[3].ParentId = null;
                break;
            case "interior-failure":
                indexed[4].ParentId = null;
                break;
            case "hidden-intermediate":
                indexed[4].Visible = false;
                break;
        }

        return (nodes.ToArray(), lower);

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

    /// <summary>Protects the fixed geometric layouts without deriving expected acceptance from coordinates.</summary>
    private static (long Upper, long Lower) DenseBoundaryCoordinates(string layout)
        => layout switch
        {
            "compact" or "compact-lower-failure" => (2, 7),
            "adjacent" or "adjacent-first-child-failure" => (20, 25),
            "lower-boundary" or "lower-boundary-failure" => (18, 21),
            "upper-boundary" => (21, 26),
            "interior" or "interior-failure" or "empty-interior" or "hidden-intermediate" => (2, 107),
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
}
