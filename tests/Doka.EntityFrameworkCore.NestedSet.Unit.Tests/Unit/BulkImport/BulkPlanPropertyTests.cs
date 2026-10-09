using FsCheck.Xunit;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks production bulk planning against independently generated adjacency and sibling-order oracles.</summary>
public sealed class BulkPlanPropertyTests
{
    /// <summary>Generated forests receive paired dense bounds, exact ancestor counts, and local sibling positions.</summary>
    /// <param name="input">Shrinkable parent selectors describing a forest.</param>
    /// <param name="boundary">The destination-coordinate input.</param>
    /// <param name="depth">The destination depth.</param>
    /// <param name="position">The destination sibling-position input.</param>
    /// <returns>A task that completes after verifying one independently generated forest.</returns>
    [Property(MaxTest = 1000)]
    public async Task GeneratedForestsPreserveGeometry(
        int[] input,
        int boundary,
        byte depth,
        int position
    )
    {
        // Arrange
        var forest = CreateForest(input);
        await using var context = CreateContext();
        var store = CreateStore(context);
        var destination = new NestedSetPlacementResolver<TreeNode, int, Guid, int>.Destination(
            1L + (uint)boundary,
            default,
            depth,
            (uint)position);

        // Act
        var plan = CreatePlan(store, forest.Roots, CancellationToken.None);
        plan.PrepareGeometry(destination, null, CancellationToken.None);

        // Assert
        AssertGeometry(plan, forest.Parents, destination, null);
        Assert.All(
            plan.Nodes,
            node => Assert.Equal(
                EntityState.Detached,
                context.Entry(node.Entity)
                    .State));
    }

    /// <summary>Native ranks reorder only sibling groups without losing parent containment or dense coordinates.</summary>
    /// <param name="input">Shrinkable parent selectors describing a forest.</param>
    /// <param name="rankInputs">Shrinkable inputs defining a complete stable ranking.</param>
    /// <returns>A task that completes after verifying one reordered forest.</returns>
    [Property(MaxTest = 1000)]
    public async Task GeneratedRanksPreserveSiblingAndParentContracts(
        int[] input,
        int[] rankInputs
    )
    {
        // Arrange
        var forest = CreateForest(input);
        await using var context = CreateContext();
        var store = CreateStore(context);
        var ranks = Enumerable
            .Range(1, forest.Parents.Length)
            .OrderBy(key => rankInputs.Length == 0 ? 0 : rankInputs[(key - 1) % rankInputs.Length])
            .ThenBy(key => key)
            .Select((key, rank) => (key, rank))
            .ToDictionary(item => item.key, item => item.rank);

        var destination = new NestedSetPlacementResolver<TreeNode, int, Guid, int>.Destination(17, default, 3, 5);

        // Act
        var plan = CreatePlan(store, forest.Roots, CancellationToken.None);
        plan.PrepareGeometry(destination, ranks, CancellationToken.None);

        // Assert
        AssertGeometry(plan, forest.Parents, destination, ranks);
    }

    /// <summary>A repeated entity is rejected whether repeated as a root or attached under a second parent.</summary>
    /// <param name="input">Shrinkable parent selectors for the otherwise valid forest.</param>
    /// <param name="secondParent">Whether the repeated entity is introduced through another parent.</param>
    /// <returns>A task that completes after checking rejection without modifying detached values.</returns>
    [Property(MaxTest = 1000)]
    public async Task RepeatedEntitiesAreRejectedBeforeStaging(
        int[] input,
        bool secondParent
    )
    {
        // Arrange
        var forest = CreateForest(input);
        await using var context = CreateContext();
        var store = CreateStore(context);
        var repeated = secondParent
            ? new NestedSetBranch<TreeNode>(new TreeNode { NodeId = forest.Parents.Length + 1 }, [forest.Roots[0]])
            : forest.Roots[0];

        NestedSetBranch<TreeNode>[] roots = [.. forest.Roots, repeated];

        // Act
        var exception = Record.Exception(() => CreatePlan(store, roots, CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(exception)
                .Code);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.All(forest.Entities, node => Assert.Equal((0L, 0L, 0, 0L), (node.Start, node.End, node.Depth, node.Position)));
    }

    /// <summary>Assigned-key collisions cannot be hidden by distinct entity references or generated topology.</summary>
    /// <param name="key">The generated assigned key, including its default value.</param>
    /// <param name="siblings">Whether colliding nodes share a parent instead of being separate roots.</param>
    /// <returns>A task that completes after verifying rejection of the duplicate key.</returns>
    [Property(MaxTest = 1000)]
    public async Task DuplicateAssignedKeysAreRejectedBeforeStaging(
        int key,
        bool siblings
    )
    {
        // Arrange
        await using var context = CreateContext();
        var store = CreateStore(context);
        var first = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = key });
        var second = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = key });
        NestedSetBranch<TreeNode>[] roots = siblings
            ? [new NestedSetBranch<TreeNode>(new TreeNode { NodeId = key ^ 1 }, [first, second])]
            : [first, second];

        // Act
        var exception = Record.Exception(() => CreatePlan(store, roots, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(exception).Code);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Builds a model-only context; generated geometry tests neither open a database nor share tracker state.</summary>
    private static TreeContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        return new TreeContext(options);
    }

    /// <summary>Binds production planning to the existing assigned-key model.</summary>
    private static NestedSetStore<TreeNode, int, Guid, int> CreateStore(
        TreeContext context
    ) => new(context, context.Model.FindEntityType(typeof(TreeNode))!, 1, Guid.Empty);

    /// <summary>Creates the production plan without introducing tracker-owned input entities.</summary>
    private static NestedSetBulkPlan<TreeNode, int, Guid, int> CreatePlan(
        NestedSetStore<TreeNode, int, Guid, int> store,
        IReadOnlyList<NestedSetBranch<TreeNode>> roots,
        CancellationToken cancellationToken
    ) => new(store, roots, new HashSet<TreeNode>(ReferenceEqualityComparer.Instance), cancellationToken);

    /// <summary>Maps shrinkable selectors to bounded acyclic adjacency, retaining input sibling order.</summary>
    private static (NestedSetBranch<TreeNode>[] Roots, TreeNode[] Entities, int[] Parents) CreateForest(
        int[] input
    )
    {
        // WHY: Bound each generated case to 64 nodes; existing capacity tests own million-node and deep-stack budgets.
        var count = Math.Clamp(input.Length, 1, 64);
        var entities = new TreeNode[count];
        var parents = new int[count];
        var branches = new NestedSetBranch<TreeNode>[count];
        var children = new List<NestedSetBranch<TreeNode>>[count];
        var roots = new List<NestedSetBranch<TreeNode>>();

        for (var index = 0; index < count; index++)
        {
            entities[index] = new TreeNode { NodeId = index + 1 };
            parents[index] = (int)((uint)(input.Length == 0 ? 0 : input[index]) % (uint)(index + 1)) - 1;
            children[index] = [];
        }

        // WHY: Every parent precedes its children, so reverse construction needs no recursion or mutable branches.
        for (var index = count - 1; index >= 0; index--)
        {
            children[index]
                .Reverse();
            branches[index] = new NestedSetBranch<TreeNode>(entities[index], children[index]);
            if (parents[index] < 0)
            {
                roots.Add(branches[index]);
            }
            else
            {
                children[parents[index]].Add(branches[index]);
            }
        }

        roots.Reverse();

        return (roots.ToArray(), entities, parents);
    }

    /// <summary>Uses input adjacency to check geometry independently of the planner's traversal links.</summary>
    private static void AssertGeometry(
        NestedSetBulkPlan<TreeNode, int, Guid, int> plan,
        int[] parents,
        NestedSetPlacementResolver<TreeNode, int, Guid, int>.Destination destination,
        Dictionary<int, int>? ranks
    )
    {
        var nodes = plan.Nodes.ToDictionary(node => node.Entity.NodeId);
        var coordinates = plan
            .Nodes
            .SelectMany(node => new[] { node.Geometry.Left, node.Geometry.Right })
            .OrderBy(value => value);

        Assert.Equal(parents.Length, nodes.Count);
        Assert.Equal(parents.Length * 2L, plan.Width);
        Assert.Equal(
            Enumerable
                .Range(0, parents.Length * 2)
                .Select(index => destination.Boundary + index),
            coordinates);

        for (var index = 0; index < parents.Length; index++)
        {
            var node = nodes[index + 1];
            var bounds = new NestedSetBounds(node.Geometry.Left, node.Geometry.Right);
            var ancestors = new HashSet<int>();
            var parent = parents[index];

            while (parent >= 0)
            {
                ancestors.Add(parent);
                parent = parents[parent];
            }

            Assert.Equal(destination.Depth + ancestors.Count, node.Geometry.Depth);
            Assert.Equal(
                parents[index] < 0 ? -1 : parents[index] + 1,
                node.Parent < 0 ? -1 : plan.Nodes[node.Parent].Entity.NodeId);

            var siblings = Enumerable
                .Range(0, parents.Length)
                .Where(candidate => parents[candidate] == parents[index])
                .OrderBy(candidate => ranks is null ? candidate : ranks[candidate + 1])
                .ToArray();

            var siblingIndex = Array.IndexOf(siblings, index);
            var expectedPosition = siblingIndex + (parents[index] < 0 ? destination.Position : 0);
            Assert.Equal(expectedPosition, node.Geometry.Position);

            if (siblingIndex > 0)
            {
                // WHY: Correct Position alone cannot detect subtree intervals assigned in a different sibling order.
                var previous = nodes[siblings[siblingIndex - 1] + 1];
                Assert.True(previous.Geometry.Right < node.Geometry.Left);
            }

            foreach (var other in nodes.Values)
            {
                var otherBounds = new NestedSetBounds(other.Geometry.Left, other.Geometry.Right);
                Assert.Equal(ancestors.Contains(other.Entity.NodeId - 1), otherBounds.Contains(bounds));
            }

            Assert.Equal(
                nodes.Values.Count(other => bounds.Contains(new NestedSetBounds(other.Geometry.Left, other.Geometry.Right))),
                bounds.DescendantCount);
        }
    }
}
