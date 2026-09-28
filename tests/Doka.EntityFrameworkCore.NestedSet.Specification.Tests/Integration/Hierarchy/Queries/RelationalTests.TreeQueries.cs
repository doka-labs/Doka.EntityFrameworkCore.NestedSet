namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Resolves the same complete tree from its root, an internal node, or a leaf.</summary>
    /// <param name="key">The root, internal, or leaf key identifying the containing tree.</param>
    /// <returns>A task that completes after verifying every branch in preorder and excluding other trees.</returns>
    [Theory]
    [InlineData(10)]
    [InlineData(70)]
    [InlineData(90)]
    public async Task TreeContainingReturnsCompleteContainingTree(
        int key
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedTreeQueryForestAsync(database);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var actual = await tree
            .TreeContaining(key)
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([10, 70, 90, 20, 30, 40], actual);
    }

    /// <summary>
    ///     Includes the selected node and its descendants while excluding ancestors and sibling branches.
    /// </summary>
    /// <param name="key">The root, internal, or leaf key identifying the inclusive subtree.</param>
    /// <param name="expected">The exact subtree keys in preorder.</param>
    /// <returns>A task that completes after comparing the selected inclusive subtree.</returns>
    [Theory]
    [InlineData(10, new[] { 10, 70, 90, 20, 30, 40 })]
    [InlineData(70, new[] { 70, 90, 20 })]
    [InlineData(90, new[] { 90 })]
    public async Task SubtreeOfReturnsInclusiveBranch(
        int key,
        int[] expected
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedTreeQueryForestAsync(database);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var actual = await tree
            .SubtreeOf(key)
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Does not resolve missing keys or foreign-scope anchors through overlapping interval values.</summary>
    /// <param name="wholeTree">Whether to query the complete containing tree instead of the inclusive subtree.</param>
    /// <param name="key">A missing key or an existing root or leaf in another scope.</param>
    /// <returns>A task that completes after verifying the query is empty.</returns>
    [Theory]
    [InlineData(true, 999)]
    [InlineData(true, 1010)]
    [InlineData(true, 1090)]
    [InlineData(false, 999)]
    [InlineData(false, 1010)]
    [InlineData(false, 1090)]
    public async Task TreeQueriesReturnEmptyForMissingOrForeignAnchors(
        bool wholeTree,
        int key
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedTreeQueryForestAsync(database);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var query = wholeTree ? tree.TreeContaining(key) : tree.SubtreeOf(key);

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Applies payload filters only to returned nodes and materializes no hidden anchor or root.</summary>
    /// <param name="wholeTree">Whether matching sibling branches belong in the result.</param>
    /// <returns>A task that completes after checking result membership, command count, and tracking.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TreeQueriesFilterResultsWithoutChangingAnchors(
        bool wholeTree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedTreeQueryForestAsync(database);
        var probe = new EnterpriseProbe();

        // WHY: The IInterceptor overload registers both command and entity-materialization observation.
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var query = wholeTree ? tree.TreeContaining(70) : tree.SubtreeOf(70);
        var expected = wholeTree
            ? new[] { 90, 20, 30, 40 }
            : new[] { 90, 20 };

        // Act
        var actual = await query
            .Where(node => node.Payload == "match")
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(node => node.NodeId));
        Assert.Equal(expected.Length, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
        var command = Assert.Single(probe.Commands);
        Assert.StartsWith("SELECT", command.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.NodeUpdates);
    }

    /// <summary>Preserves preorder under projection and pagination without materializing domain entities.</summary>
    /// <param name="wholeTree">Whether the page starts from the complete tree or the inclusive subtree.</param>
    /// <returns>A task that completes after checking the projected page and its database execution.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TreeQueriesProjectAndPageInPreorder(
        bool wholeTree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedTreeQueryForestAsync(database);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var query = wholeTree ? tree.TreeContaining(70) : tree.SubtreeOf(70);
        var expected = wholeTree
            ? new[] { (Id: 70, Depth: 1), (Id: 90, Depth: 2) }
            : new[] { (Id: 90, Depth: 2), (Id: 20, Depth: 2) };

        // Act
        var actual = await query
            .Select(node => new
            {
                node.NodeId,
                node.Depth,
            })
            .Skip(1)
            .Take(2)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(node => (node.NodeId, node.Depth)));
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
        var command = Assert.Single(probe.Commands);
        Assert.DoesNotContain(nameof(TreeNode.Payload), command, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Defers root and interval resolution until execution after the anchor has moved to another tree.
    /// </summary>
    /// <param name="wholeTree">Whether to resolve the new containing tree or the moved inclusive subtree.</param>
    /// <returns>A task that completes after verifying the query observes the committed move.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TreeQueriesAreDeferredAndFollowMovesBeforeExecution(
        bool wholeTree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedTreeQueryForestAsync(database);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var query = wholeTree ? tree.TreeContaining(70) : tree.SubtreeOf(70);
        var commandsAfterConstruction = probe.Commands.Count;
        var materializationsAfterConstruction = probe.MaterializedNodes;
        await using var writer = database.CreateContext();
        var editor = writer
            .NestedSet<TreeNode>()
            .ForScope(1);

        // WHY: Moving through an independent context changes the persisted root and bounds before query execution.
        await editor.MoveToAsync(70, 200, CancellationToken.None);
        var expected = wholeTree
            ? new[] { 200, 210, 70, 90, 20 }
            : new[] { 70, 90, 20 };

        // Act
        var actual = await query
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, commandsAfterConstruction);
        Assert.Equal(0, materializationsAfterConstruction);
        Assert.Equal(expected, actual);
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Resolves array keys by their database value instead of CLR reference identity.</summary>
    /// <param name="wholeTree">Whether the complete tree or inclusive subtree is requested.</param>
    /// <returns>A task that completes after matching a separately allocated binary anchor key.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TreeQueriesUseDatabaseEqualityForBinaryKeys(
        bool wholeTree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedBinaryTreeQueryForestAsync(database);
        var probe = new EnterpriseProbe(nameof(BinaryNode));
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);

        // WHY: This key has the stored bytes but cannot share an object reference with any seeded entity.
        var key = new byte[] { 3, 4 };
        var query = wholeTree ? tree.TreeContaining(key) : tree.SubtreeOf(key);
        var expected = wholeTree
            ? new[] { "0102", "0304", "0506", "0708" }
            : new[] { "0304", "0506" };

        // Act
        var actual = await query
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(Convert.ToHexString));
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Seeds two roots and a second scope whose valid coordinates overlap the first tree.</summary>
    /// <param name="database">The reset database receiving independently known structural values.</param>
    /// <returns>A task that completes after the seed context has saved and released its tracked entities.</returns>
    private static async Task SeedTreeQueryForestAsync(
        TestDatabase database
    )
    {
        // WHY: Direct coordinates isolate query behavior from mutation correctness.
        // Keys deliberately differ from preorder so an incidental primary-key sort cannot pass.
        var nodes = new[]
        {
            new TreeNode
            {
                NodeId = 10,
                Tree = 1,
                Start = 1,
                End = 12,
                Payload = "hidden",
            },
            new TreeNode
            {
                NodeId = 70,
                Tree = 1,
                Start = 2,
                End = 7,
                Parent = 10,
                Depth = 1,
                Payload = "hidden",
            },
            new TreeNode
            {
                NodeId = 90,
                Tree = 1,
                Start = 3,
                End = 4,
                Parent = 70,
                Depth = 2,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 20,
                Tree = 1,
                Start = 5,
                End = 6,
                Parent = 70,
                Depth = 2,
                Position = 1,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 30,
                Tree = 1,
                Start = 8,
                End = 11,
                Parent = 10,
                Depth = 1,
                Position = 1,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 40,
                Tree = 1,
                Start = 9,
                End = 10,
                Parent = 30,
                Depth = 2,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 200,
                Tree = 1,
                TreeId = new Guid("00000000-0000-0000-0000-000000000002"),
                Start = 1,
                End = 4,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 210,
                Tree = 1,
                TreeId = new Guid("00000000-0000-0000-0000-000000000002"),
                Start = 2,
                End = 3,
                Parent = 200,
                Depth = 1,
                Payload = "match",
            },
        };

        // WHY: Matching payload and intervals in another scope challenge every anchor, root, and result filter.
        var foreignNodes = nodes
            .Take(6)
            .Select(node => new TreeNode
            {
                NodeId = node.NodeId + 1000,
                Tree = 2,
                Start = node.Start,
                End = node.End,
                Parent = node.Parent + 1000,
                Depth = node.Depth,
                Position = node.Position,
                Payload = "match",
            });

        await using var context = database.CreateContext();
        await context.AddRangeAsync(nodes.Concat(foreignNodes), CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
    }

    /// <summary>Seeds binary-key branches plus unrelated roots in the same and another scope.</summary>
    /// <param name="database">The reset database whose existing model supports binary primary keys.</param>
    /// <returns>A task that completes after storing the independent binary-key forest.</returns>
    private static async Task SeedBinaryTreeQueryForestAsync(
        TestDatabase database
    )
    {
        await using var context = database.CreateContext();
        await context.AddRangeAsync(
            [
                new BinaryNode
                {
                    Id = [1, 2],
                    Tree = 1,
                    Left = 1,
                    Right = 8,
                },
                new BinaryNode
                {
                    Id = [3, 4],
                    Tree = 1,
                    Left = 2,
                    Right = 5,
                    ParentId = [1, 2],
                    Depth = 1,
                },
                new BinaryNode
                {
                    Id = [5, 6],
                    Tree = 1,
                    Left = 3,
                    Right = 4,
                    ParentId = [3, 4],
                    Depth = 2,
                },
                new BinaryNode
                {
                    Id = [7, 8],
                    Tree = 1,
                    Left = 6,
                    Right = 7,
                    ParentId = [1, 2],
                    Depth = 1,
                    Position = 1,
                },
                new BinaryNode
                {
                    Id = [9, 10],
                    Tree = 1,
                    TreeId = new Guid("00000000-0000-0000-0000-000000000002"),
                    Left = 1,
                    Right = 2,
                },
                new BinaryNode
                {
                    Id = [11, 12],
                    Tree = 2,
                    Left = 1,
                    Right = 4,
                },
                new BinaryNode
                {
                    Id = [13, 14],
                    Tree = 2,
                    Left = 2,
                    Right = 3,
                    ParentId = [11, 12],
                    Depth = 1,
                },
            ],
            CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
    }
}
