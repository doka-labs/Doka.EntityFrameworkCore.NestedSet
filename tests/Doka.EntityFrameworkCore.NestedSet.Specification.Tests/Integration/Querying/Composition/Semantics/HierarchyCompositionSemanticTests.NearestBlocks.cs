namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Independent nearest selection follows ancestors rather than key order or off-path candidates.</summary>
    /// <param name="mode">The owner section, rejected decision, missing owner, or visibility adversary.</param>
    /// <param name="expectedCount">The independently known accepted original occurrence count.</param>
    [Theory]
    [InlineData("section-owner", 16)]
    [InlineData("zero-owner", 16)]
    [InlineData("zero-owner-rejects", 8)]
    [InlineData("section-owner-rejects", 8)]
    [InlineData("own-section-owner-rejects", 8)]
    [InlineData("none", 0)]
    [InlineData("only-off-path", 0)]
    [InlineData("hidden-above-owner", 16)]
    [InlineData("hidden-between", 8)]
    public async Task IndependentNearestSelectionPreservesStructuralOwnerAndOriginalBag(
        string mode,
        int expectedCount
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var local = NearestSectionNodes(scope, treeId, mode);
        var foreign = DensePathDecoys(local, Guid.NewGuid(), treeId, 0)
            .Concat(DensePathDecoys(local, scope, Guid.NewGuid(), 10_000)).ToArray();

        foreach (var node in foreign)
        {
            node.Candidate = true;
            node.Matches = true;
        }

        var all = local.Concat(foreign).ToArray();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(scope, 40, all, probe);
        await context.AddRangeAsync(LongDays().Select(day => new CompositionInputOne
        {
            Scope = scope,
            NodeId = 80,
            Date = day,
            Amount = 1,
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(treeId);
        var original = LongRows(context, scope, 900, 900, 40)
            .Concat(LongRows(context, scope, 0, 0, 40))
            .Concat(LongRows(context, scope, 900, 900, 80))
            .Concat(LongRows(context, scope, 0, 0, 80));

        var rows = original.Concat(original);
        var expected = NearestSectionOracle(local);

        // Act
        // WHY: Both criteria deliberately ignore the original row. Row-correlated candidate tests
        // cannot exercise the independent candidate relation qualified by these cases.
        var query = hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
            (row, owner) => owner.Candidate, (row, owner) => owner.Matches);

        var actualRows = await ReadLongRowsAsync(query);
        var actual = actualRows.OrderBy(row => row.Origin).ThenBy(row => row.Node).ThenBy(row => row.Day).ToArray();

        // Assert
        Assert.False(local.Single(node => node.Id == 40).Candidate);
        Assert.False(local.Single(node => node.Id == 80).Candidate);
        Assert.Equal((1L, 400L), (local[0].Left, local[0].Right));
        Assert.Equal(expectedCount, expected.Length);
        Assert.Equal(expected, actual);
        Assert.All(actual.GroupBy(row => row), occurrences => Assert.Equal(2, occurrences.Count()));
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>Independent owner selection preserves every occurrence of a volatile paged source.</summary>
    [Fact]
    public async Task IndependentNearestPreservesVolatilePagedSourceCardinality()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var local = NearestSectionNodes(scope, treeId, "zero-owner");

        foreach (var node in local)
        {
            node.Visible = true;
            node.Candidate = node.Id == 900;
            node.Matches = true;
        }

        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(scope, 40, local, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(treeId);
        var source = hierarchy.Nodes.OrderBy(_ => EF.Functions.Random()).Take(10);

        // Act
        // WHY: Every sampled node, including the root itself, has the same matching owner. Reusing the
        // source for owner selection must not independently resample it and drop original occurrences.
        var actual = await hierarchy.WhereNearestAncestorOrSelf(source, node => node,
                (node, owner) => owner.Id == 900, (node, owner) => true)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(10, actual.Length);
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>Shared independent owner choices preserve each occurrence's value and prior paging boundary.</summary>
    /// <param name="paged">Whether the original occurrences are selected by an upstream input window.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentNearestChoiceKeepsRowValuesAndUpstreamPaging(
        bool paged
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var local = NearestSectionNodes(scope, treeId, "zero-owner");
        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(scope, 40, local, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(treeId);
        var nodes = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 40);
        var firstDay = LongDays()[0];
        var original = context.Set<CompositionInputOne>().Where(fact => fact.Scope == scope && fact.NodeId == 40)
            .Join(nodes, fact => fact.NodeId, node => node.Id, (fact, node) => new
            {
                Node = node,
                Origin = fact.Date == firstDay ? 101 : 202,
                AcceptedOwnerId = fact.Date == firstDay ? 0 : 7,
            });

        var rows = original.Concat(original);

        if (paged)
        {
            // WHY: Every marker would pass the owner decision. The input filter and window, rather than
            // nearest rejection, must remove them before the original occurrences reach the owner filter.
            rows = nodes.Select(node => new { Node = node, Origin = 1, AcceptedOwnerId = 0 })
                .Concat(nodes.Select(node => new { Node = node, Origin = 999, AcceptedOwnerId = 0 }))
                .Concat(rows)
                .Concat(nodes.Select(node => new { Node = node, Origin = 150, AcceptedOwnerId = 0 }))
                .Where(row => row.Origin != 150)
                .OrderBy(row => row.Origin)
                .Skip(1)
                .Take(4);
        }

        // Act
        // WHY: Only candidate selection may be shared for the same lower. The value predicate still
        // belongs to each original occurrence, including the row that asks for a farther eligible owner.
        var actual = await hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => owner.Id == row.AcceptedOwnerId)
            .OrderBy(row => row.Origin)
            .Select(row => new { row.Origin, Lower = row.Node.Id, row.AcceptedOwnerId })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, actual.Length);
        Assert.All(actual, row => Assert.Equal((101, 40, 0), (row.Origin, row.Lower, row.AcceptedOwnerId)));
        AssertLongReadBoundary(context, probe);
    }

    /// <summary>Creates several ancestor sections with nonmonotonic keys and intervening off-path candidates.</summary>
    private static CompositionNode[] NearestSectionNodes(Guid scope, Guid treeId, string mode)
    {
        var nodes = new List<CompositionNode>();
        Add(900, null);
        Add(7, 900);
        Add(0, 7);

        for (var index = 0; index < 50; index++)
        {
            Add(1_000 + index, 0);
        }

        Add(70, 0);
        Add(40, 70);
        Add(200, 70);
        Add(30, 0);
        Add(80, 30);

        while (nodes.Count < 200)
        {
            Add(3_000 + nodes.Count, 200);
        }

        var children = nodes.ToLookup(node => node.ParentId);
        var next = 1L;
        Visit(nodes[0], 0, 0);
        var indexed = nodes.ToDictionary(node => node.Id);
        indexed[0].Matches = true;
        indexed[30].Matches = true;

        switch (mode)
        {
            case "section-owner":
                indexed[70].Candidate = true;
                indexed[70].Matches = true;
                indexed[0].Matches = false;
                break;
            case "zero-owner":
                break;
            case "zero-owner-rejects":
                indexed[0].Matches = false;
                indexed[7].Matches = true;
                indexed[900].Matches = true;
                break;
            case "section-owner-rejects":
                indexed[70].Candidate = true;
                indexed[7].Matches = true;
                indexed[900].Matches = true;
                break;
            case "own-section-owner-rejects":
                indexed[30].Matches = false;
                indexed[7].Matches = true;
                indexed[900].Matches = true;
                break;
            case "none":
                foreach (var node in nodes)
                {
                    node.Candidate = false;
                }

                break;
            case "only-off-path":
                foreach (var key in new[] { 900, 7, 0, 70, 30 })
                {
                    indexed[key].Candidate = false;
                }

                break;
            case "hidden-above-owner":
                indexed[7].Visible = false;
                break;
            case "hidden-between":
                indexed[70].Visible = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
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
                Candidate = id is not (40 or 70 or 80),
                Matches = id >= 1_000 || id == 200,
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

    /// <summary>Chooses the first visible Parent-walk candidate before checking its value and visible suffix.</summary>
    private static LongPathOccurrence[] NearestSectionOracle(CompositionNode[] nodes)
    {
        var indexed = nodes.ToDictionary(node => node.Id);
        var parents = nodes.ToDictionary(node => node.Id, node => node.ParentId);
        var result = new List<LongPathOccurrence>();

        foreach (var lower in new[] { 40, 80 })
        {
            var owner = Ancestors(parents, lower, includeSelf: true)
                .Where(key => indexed[key].Visible && indexed[key].Candidate)
                .Select(key => (int?)key).FirstOrDefault();

            if (owner is not { } selected || !indexed[selected].Matches
                || Ancestors(parents, lower, includeSelf: true).TakeWhile(key => key != selected)
                    .Any(key => !indexed[key].Visible))
            {
                continue;
            }

            foreach (var origin in new[] { 900, 0 })
            {
                foreach (var day in LongDays())
                {
                    result.Add(new LongPathOccurrence(origin, lower, day));
                }
            }
        }

        return result.SelectMany(row => new[] { row, row })
            .OrderBy(row => row.Origin).ThenBy(row => row.Node).ThenBy(row => row.Day).ToArray();
    }
}
