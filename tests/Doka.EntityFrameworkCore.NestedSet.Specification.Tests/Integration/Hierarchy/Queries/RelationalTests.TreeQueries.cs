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
    public async Task TreeContaining_ExistingAnchor_ReturnsCompleteContainingTree(
        int key
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeQueryForestAsync(database, cancellationToken: TestContext.Current.CancellationToken);
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
    [InlineData(
        10,
        new[]
        {
            10,
            70,
            90,
            20,
            30,
            40,
        })]
    [InlineData(70, new[] { 70, 90, 20 })]
    [InlineData(90, new[] { 90 })]
    public async Task SubtreeOf_ExistingAnchor_ReturnsInclusiveBranch(
        int key,
        int[] expected
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeQueryForestAsync(database, cancellationToken: TestContext.Current.CancellationToken);
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
    public async Task TreeContainingOrSubtreeOf_MissingOrForeignAnchors_ReturnEmptyQueries(
        bool wholeTree,
        int key
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeQueryForestAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var query = wholeTree
            ? tree.TreeContaining(key)
            : tree.SubtreeOf(key);

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
    public async Task Where_TreeQueries_FilterResultsWithoutChangingAnchors(
        bool wholeTree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeQueryForestAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new EnterpriseProbe();

        // WHY: The IInterceptor overload registers both command and entity-materialization observation.
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var query = wholeTree
            ? tree.TreeContaining(70)
            : tree.SubtreeOf(70);

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
    public async Task SubtreeOf_TreeQueries_ProjectAndPageInPreorder(
        bool wholeTree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeQueryForestAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var query = wholeTree
            ? tree.TreeContaining(70)
            : tree.SubtreeOf(70);

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
    public async Task SubtreeOf_TreeQueries_AreDeferredAndFollowMovesBeforeExecution(
        bool wholeTree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeQueryForestAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var query = wholeTree
            ? tree.TreeContaining(70)
            : tree.SubtreeOf(70);

        var commandsAfterConstruction = probe.Commands.Count;
        var materializationsAfterConstruction = probe.MaterializedNodes;
        await using var writer = database.CreateContext();
        var editor = writer
            .NestedSet<TreeNode>()
            .ForScope(1);

        // WHY: Moving through an independent context changes the persisted root and bounds before query execution.
        await editor.MoveToAsync(70, 200, CancellationToken.None);
        var expected = wholeTree
            ? new[]
            {
                200,
                210,
                70,
                90,
                20,
            }
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
    public async Task SubtreeOf_TreeQueries_UseDatabaseEqualityForBinaryKeys(
        bool wholeTree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await SeedBinaryTreeQueryForestAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new EnterpriseProbe(nameof(BinaryNode));
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);

        // WHY: This key has the stored bytes but cannot share an object reference with any seeded entity.
        var key = new byte[] { 3, 4 };
        var query = wholeTree
            ? tree.TreeContaining(key)
            : tree.SubtreeOf(key);

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
}
