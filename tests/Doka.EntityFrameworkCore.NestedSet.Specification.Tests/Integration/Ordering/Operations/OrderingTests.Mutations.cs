namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Verifies automatic insertion orders every sibling group and preserves whole-tree preorder.</summary>
    [Fact]
    public async Task OutOfOrderInsertsMaintainEveryLevel()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);

        // Act
        await SeedAsync(tree);
        await tree.InsertChildAsync(Node(7, "Zulu"), 2, CancellationToken.None);
        await tree.InsertChildAsync(Node(6, "Alpha"), 2, CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.TreeContaining(5), 1, 2, 6, 7, 3, 5, 4);
        await AssertIdsAsync(tree.SubtreeOf(2), 2, 6, 7);
        await AssertForestAsync(context);
    }

    /// <summary>Verifies automatic child placement uses domain order rather than append order.</summary>
    [Fact]
    public async Task ChildrenAreInsertedInConfiguredOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        await tree.InsertRootAsync(Node(10, "Root"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(1, "Zulu"), 10, CancellationToken.None);
        await tree.InsertChildAsync(Node(2, "Alpha"), 10, CancellationToken.None);

        // Act
        await tree.InsertChildAsync(Node(3, "Middle"), 10, CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.ChildrenOf(10), 2, 3, 1);
        await AssertForestAsync(context);
    }

    /// <summary>Verifies equal configured values fall back to ascending primary-key order.</summary>
    [Fact]
    public async Task EqualValuesUseAscendingPrimaryKeyTieBreaker()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        await tree.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(8, "Same", 9), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(2, "Same", 9), 1, CancellationToken.None);

        // Act
        await tree.InsertChildAsync(Node(5, "Same", 9), 1, CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.ChildrenOf(1), 2, 5, 8);
        await AssertForestAsync(context);
    }

    /// <summary>
    ///     Verifies null placement, case comparison, descending secondary values, and ties use database ordering.
    /// </summary>
    [Fact]
    public async Task MixedDirectionsAndNullsMatchNativeDatabaseOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        OrderingNode[] nodes =
        [
            Node(9, "beta", 2),
            Node(8, null, 3),
            Node(7, "Alpha", 1),
            Node(6, "alpha", 8),
            Node(5, null, 9),
            Node(4, "Alpha", 1),
            Node(3, "Beta", 6),
        ];

        await tree.InsertRootAsync(Node(100, "Root"), Guid.NewGuid(), CancellationToken.None);

        // Act
        foreach (var node in nodes)
        {
            await tree.InsertChildAsync(node, 100, CancellationToken.None);
        }

        // Assert
        // WHY: The explicit rank removes provider-dependent null defaults; the database remains the collation oracle.
        var expected = await Nodes(context)
            .Where(node => node.ParentId == 100)
            .OrderBy(node => node.Name == null ? 1 : 0)
            .ThenBy(node => node.Name)
            .ThenByDescending(node => node.Priority)
            .ThenBy(node => node.Id)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(expected, await IdsAsync(tree.ChildrenOf(100)));
        await AssertForestAsync(context);
    }

    /// <summary>Verifies converted sort values follow provider values rather than CLR enum ordinals.</summary>
    [Fact]
    public async Task ConvertedPayloadSortUsesPersistedRepresentation()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine, "Converted");
        var tree = Tree(context);
        var zeta = Node(1, "Same");
        zeta.Category = OrderingCategory.Zeta;
        var alpha = Node(2, "Same");
        alpha.Category = OrderingCategory.Alpha;
        var beta = Node(3, "Same");
        beta.Category = OrderingCategory.Beta;
        await tree.InsertRootAsync(Node(10, "Root"), Guid.NewGuid(), CancellationToken.None);

        // Act
        await tree.InsertChildAsync(zeta, 10, CancellationToken.None);
        await tree.InsertChildAsync(alpha, 10, CancellationToken.None);
        await tree.InsertChildAsync(beta, 10, CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.ChildrenOf(10), 2, 3, 1);
        await AssertForestAsync(context);
    }

    /// <summary>Verifies automatic reparenting moves the subtree into its sorted sibling location.</summary>
    [Fact]
    public async Task MoveToUsesDestinationOrderAndRetainsDescendants()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        await SeedAsync(tree);
        await tree.InsertChildAsync(Node(6, "Charlie"), 2, CancellationToken.None);

        // Act
        await tree.MoveToAsync(3, 2, CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.ChildrenOf(2), 3, 6);
        await AssertIdsAsync(tree.SubtreeOf(3), 3, 5);
        Assert.Equal(3, await Nodes(context)
            .Where(node => node.Id == 5)
            .Select(node => node.Depth)
            .SingleAsync(CancellationToken.None));
        await AssertForestAsync(context);
    }

    /// <summary>
    ///     Verifies detaching an ordered subtree creates independent local bounds and preserves descendants.
    /// </summary>
    [Fact]
    public async Task DetachAsTreePreservesOrderedSubtree()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        await SeedAsync(tree);
        var detachedTreeId = Guid.NewGuid();

        // Act
        await tree.DetachAsTreeAsync(3, detachedTreeId, CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.InTree(detachedTreeId).Nodes, 3, 5);
        await AssertIdsAsync(tree.TreeContaining(1), 1, 2, 4);
        await AssertIdsAsync(tree.TreeContaining(5), 3, 5);
        await AssertForestAsync(context);
    }

    /// <summary>Enumerates every explicit placement API for every real provider.</summary>
    public static IEnumerable<object[]> ExplicitPlacements
    {
        get
        {
            foreach (var placement in new[]
                     {
                         "InsertFirst", "InsertLast", "InsertBefore", "InsertAfter",
                         "MoveSubtreeFirst", "MoveSubtreeLast", "MoveBefore", "MoveAfter",
                     })
            {
                yield return [placement];
            }
        }
    }

    /// <summary>Verifies strict mode rejects each positional override without changing any row.</summary>
    [Theory]
    [MemberData(nameof(ExplicitPlacements))]
    public async Task StrictModeRejectsExplicitPlacement(
        string placement
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        await SeedAsync(tree);
        var before = await SnapshotAsync(context);

        // Act
        var exception = await Record.ExceptionAsync(() => PlaceAsync(tree, placement));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        AssertSnapshot(before, await SnapshotAsync(context));
    }

    /// <summary>
    ///     Verifies flexible mode accepts each explicit placement rather than enforcing strict mode globally.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExplicitPlacements))]
    public async Task FlexibleModeAllowsExplicitPlacement(
        string placement
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine, "Flexible");
        var tree = Tree(context);
        await SeedAsync(tree);

        // Act
        await PlaceAsync(tree, placement);

        // Assert
        int[] expected = placement switch
        {
            "InsertFirst" => [9, 2, 3, 4],
            "InsertLast" => [2, 3, 4, 9],
            "InsertBefore" => [2, 9, 3, 4],
            "InsertAfter" => [2, 3, 9, 4],
            "MoveSubtreeFirst" => [3, 2, 4],
            "MoveSubtreeLast" => [2, 4, 3],
            "MoveBefore" => [4, 2, 3],
            "MoveAfter" => [3, 4, 2],
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };

        Assert.Equal(expected, await IdsAsync(tree.ChildrenOf(1)));
        await AssertForestAsync(context);
    }

    /// <summary>Invokes public placement APIs for leaf and subtree moves, including both edge positions.</summary>
    private static Task PlaceAsync(
        ScopedNestedSet<OrderingNode, int> tree,
        string placement
    ) => placement switch
    {
        "InsertFirst" => tree.InsertAsFirstChildAsync(Node(9, "Delta"), 1, CancellationToken.None),
        "InsertLast" => tree.InsertAsLastChildAsync(Node(9, "Delta"), 1, CancellationToken.None),
        "InsertBefore" => tree.InsertBeforeAsync(Node(9, "Delta"), 3, CancellationToken.None),
        "InsertAfter" => tree.InsertAfterAsync(Node(9, "Delta"), 3, CancellationToken.None),
        "MoveSubtreeFirst" => tree.MoveBeforeAsync(3, 2, CancellationToken.None),
        "MoveSubtreeLast" => tree.MoveAfterAsync(3, 4, CancellationToken.None),
        "MoveBefore" => tree.MoveBeforeAsync(4, 2, CancellationToken.None),
        "MoveAfter" => tree.MoveAfterAsync(2, 4, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(placement)),
    };
}
