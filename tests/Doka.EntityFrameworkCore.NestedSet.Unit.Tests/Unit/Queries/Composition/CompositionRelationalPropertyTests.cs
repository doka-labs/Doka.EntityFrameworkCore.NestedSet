using FsCheck.Xunit;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks generated composition bags against adjacency through actual SQLite SQL.</summary>
public sealed class CompositionRelationalPropertyTests
{
    /// <summary>Generated forests preserve direction, self inclusion, exact distance and repeated rows.</summary>
    /// <param name="input">Shrinkable selectors that vary each additional node's preceding parent.</param>
    /// <param name="selector">The generated anchor parity and source multiplicity.</param>
    /// <param name="upward">Whether the query projects ancestors instead of descendants.</param>
    /// <param name="includeSelf">Whether zero-distance pairs are retained.</param>
    /// <returns>A task completing after one real SQL result is compared with its independent adjacency bag.</returns>
    [Trait("Category", "Relational")]
    [Property(MaxTest = 64, StartSize = 0, EndSize = 24, Replay = "20261016,104729")]
    public async Task GeneratedSqlResultsMatchIndependentAdjacencyBags(
        int[] input,
        int selector,
        bool upward,
        bool includeSelf
    )
    {
        // Arrange
        var (nodes, parents) = CreateForest(input);
        var parity = (int)((uint)selector % 2);
        var copies = 1 + (int)((uint)selector % 3);
        var expected = ExpectedBag(parents, parity, copies, upward, includeSelf);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CompositionGuardContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets())
            .Options;

        await using var context = new CompositionGuardContext(options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping)
            .AddRangeAsync(nodes, CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        // WHY: Keep set-operation inputs as scalar projections; construct assertion records only after Concat.
        var pairs = upward
            ? includeSelf
                ? hierarchy.SelectAncestorsOrSelf(node => node.Id % 2 == parity,
                    (origin, ancestor, distance) => new
                    {
                        Origin = origin.Id,
                        Related = ancestor.Id,
                        Distance = distance,
                    })
                : hierarchy.SelectAncestors(node => node.Id % 2 == parity,
                    (origin, ancestor, distance) => new
                    {
                        Origin = origin.Id,
                        Related = ancestor.Id,
                        Distance = distance,
                    })
            : hierarchy.SelectSubtrees(node => node.Id % 2 == parity,
                (origin, descendant, distance) => new
                {
                    Origin = origin.Id,
                    Related = descendant.Id,
                    Distance = distance,
                });

        if (!upward && !includeSelf)
        {
            pairs = pairs.Where(pair => pair.Distance > 0);
        }

        var query = pairs;

        for (var copy = 1; copy < copies; copy++)
        {
            query = query.Concat(pairs);
        }

        // Act
        var actual = await query
            .Select(pair => new RelationalPair(pair.Origin, pair.Related, pair.Distance))
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            expected.OrderBy(pair => pair.Origin).ThenBy(pair => pair.Related).ThenBy(pair => pair.Distance),
            actual.OrderBy(pair => pair.Origin).ThenBy(pair => pair.Related).ThenBy(pair => pair.Distance));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Creates small valid coordinates while keeping the parent array as an independent read oracle.</summary>
    private static (CompositionGuardNode[] Nodes, int[] Parents) CreateForest(
        int[] input
    )
    {
        // WHY: This is a generated SQL seam, not a capacity test. Every case retains a three-node chain
        // and an independent tree with reused bounds; additional topology stays bounded at twenty nodes.
        var count = 4 + Math.Min(input.Length, 16);
        var parents = new int[count];
        var nodes = new CompositionGuardNode[count];
        parents[0] = -1;
        parents[1] = 0;
        parents[2] = 1;
        parents[3] = -1;

        for (var index = 4; index < count; index++)
        {
            parents[index] = (int)((uint)input[index - 4] % (uint)(index + 1)) - 1;
        }

        for (var index = 0; index < count; index++)
        {
            nodes[index] = new CompositionGuardNode
            {
                Id = index + 1,
                ParentId = parents[index] < 0 ? null : parents[index] + 1,
            };
        }

        for (var root = 0; root < count; root++)
        {
            if (parents[root] >= 0)
            {
                continue;
            }

            var treeId = new Guid(root + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var boundary = 1L;
            Visit(root, 0, 0, treeId, ref boundary);
        }

        return (nodes, parents);

        void Visit(
            int index,
            int depth,
            long position,
            Guid treeId,
            ref long boundary
        )
        {
            var node = nodes[index];
            node.TreeId = treeId;
            node.Depth = depth;
            node.Position = position;
            node.Left = boundary++;
            var childPosition = 0L;

            for (var child = index + 1; child < count; child++)
            {
                if (parents[child] == index)
                {
                    Visit(child, depth + 1, childPosition++, treeId, ref boundary);
                }
            }

            node.Right = boundary++;
        }
    }

    /// <summary>Builds expected pairs solely from adjacency, without reading bounds or stored depth.</summary>
    private static List<RelationalPair> ExpectedBag(
        int[] parents,
        int parity,
        int copies,
        bool upward,
        bool includeSelf
    )
    {
        var expected = new List<RelationalPair>();

        for (var origin = 0; origin < parents.Length; origin++)
        {
            if ((origin + 1) % 2 != parity)
            {
                continue;
            }

            for (var related = 0; related < parents.Length; related++)
            {
                var upper = upward ? related : origin;
                var current = upward ? origin : related;
                var distance = 0;

                while (current >= 0 && current != upper)
                {
                    current = parents[current];
                    distance++;
                }

                if (current < 0 || (!includeSelf && distance == 0))
                {
                    continue;
                }

                for (var copy = 0; copy < copies; copy++)
                {
                    expected.Add(new RelationalPair(origin + 1, related + 1, distance));
                }
            }
        }

        return expected;
    }

    /// <summary>Identifies one occurrence in the scalar query result, including its structural distance.</summary>
    /// <param name="Origin">The selected source node.</param>
    /// <param name="Related">The ancestor or descendant paired with that source.</param>
    /// <param name="Distance">The number of direct parent links between the two endpoints.</param>
    private readonly record struct RelationalPair(
        int Origin,
        int Related,
        int Distance
    );
}
