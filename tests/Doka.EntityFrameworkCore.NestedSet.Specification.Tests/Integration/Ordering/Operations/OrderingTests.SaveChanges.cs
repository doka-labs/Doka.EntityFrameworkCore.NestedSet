namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>
    ///     Verifies changing a descending secondary value repositions its subtree among primary-value ties.
    /// </summary>
    [Fact]
    public async Task SecondarySortChangeRepositionsTheSubtree()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        await tree.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(2, "Same", 10), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(3, "Same", 30), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(4, "Same", 20), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(5, "Leaf"), 2, CancellationToken.None);
        var changed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.Priority = 40;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(Engine);
        await AssertIdsAsync(Tree(verification).ChildrenOf(1), 2, 3, 4);
        await AssertIdsAsync(Tree(verification).SubtreeOf(2), 2, 5);
        await AssertForestAsync(verification);
    }

    /// <summary>Verifies a rename to null follows the configured provider-independent null-last order.</summary>
    [Fact]
    public async Task NullSortChangeMatchesNativeSiblingOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(context));
        var changed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        changed.Name = null;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var tree = Tree(verification);
        var expected = await Nodes(verification)
            .Where(node => node.ParentId == 1)
            .OrderBy(node => node.Name == null ? 1 : 0)
            .ThenBy(node => node.Name)
            .ThenByDescending(node => node.Priority)
            .ThenBy(node => node.Id)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(expected, await IdsAsync(tree.ChildrenOf(1)));
        await AssertIdsAsync(tree.SubtreeOf(3), 3, 5);
        await AssertForestAsync(verification);
    }

    /// <summary>Verifies an ordinary rename moves a complete subtree and refreshes every tracked coordinate.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RenameRefreshesTrackedSubtreesAndHonorsAcceptance(
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(context));
        var tracked = await context.Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var renamed = tracked.Single(node => node.Id == 3);
        renamed.Name = "Zulu";

        // Act
        var saved = await context.SaveChangesAsync(acceptAllChangesOnSuccess, CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        var entry = context.Entry(renamed);
        Assert.Equal(acceptAllChangesOnSuccess ? EntityState.Unchanged : EntityState.Modified, entry.State);
        Assert.Equal(acceptAllChangesOnSuccess ? "Zulu" : "Bravo", entry.Property(node => node.Name).OriginalValue);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var persisted = await SnapshotAsync(verification);
        AssertSnapshot(persisted, tracked);
        await AssertIdsAsync(Tree(verification).ChildrenOf(1), 2, 4, 3);
        await AssertIdsAsync(Tree(verification).SubtreeOf(3), 3, 5);
        await AssertForestAsync(verification);
    }

    /// <summary>Verifies several pending sort changes are coordinated from their final values in one save.</summary>
    [Fact]
    public async Task MultipleRenamesProduceOneConsistentSiblingOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(context));
        var nodes = await context.Set<OrderingNode>()
            .ToDictionaryAsync(node => node.Id, CancellationToken.None);

        nodes[2].Name = "Zulu";
        nodes[3].Name = "Middle";
        nodes[4].Name = "Aaron";

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, saved);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        await AssertIdsAsync(Tree(verification).ChildrenOf(1), 4, 3, 2);
        await AssertIdsAsync(Tree(verification).TreeContaining(5), 1, 4, 3, 5, 2);
        await AssertForestAsync(verification);
    }

    /// <summary>Verifies inserting an unrelated row shares the same successful payload and ordering save.</summary>
    [Fact]
    public async Task SavePersistsPendingApplicationDataWithTheReorder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(context));
        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";
        await context.AddAsync(new OrderingMarker { Id = 1, Value = "domain event" }, CancellationToken.None);

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, saved);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        Assert.Equal("domain event", await verification.Set<OrderingMarker>()
            .Select(row => row.Value)
            .SingleAsync(CancellationToken.None));
        await AssertIdsAsync(Tree(verification).ChildrenOf(1), 2, 4, 3);
        await AssertForestAsync(verification);
    }

    /// <summary>Verifies rename ordering does not disturb identical data in another forest scope.</summary>
    [Fact]
    public async Task RenameLeavesAnotherScopeUnchanged()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(context));
        var other = Tree(context, 2);
        await other.InsertRootAsync(Node(11, "Root"), Guid.NewGuid(), CancellationToken.None);
        await other.InsertChildAsync(Node(12, "Zulu"), 11, CancellationToken.None);
        await other.InsertChildAsync(Node(13, "Alpha"), 11, CancellationToken.None);
        var before = await Nodes(context, 2)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(Engine);
        AssertSnapshot(before, await Nodes(verification, 2)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None));
        await AssertIdsAsync(Tree(verification).ChildrenOf(1), 2, 4, 3);
        await AssertForestAsync(verification);
        await AssertForestAsync(verification, 2);
    }

    /// <summary>
    ///     Reorders one TreeId without changing a tracked tree with overlapping bounds in the same Scope.
    /// </summary>
    [Fact]
    public async Task RenameLeavesAnotherTreeInTheSameScopeUnchanged()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.NestedSet<OrderingNode>().ForScope(1);
        var changedTreeId = Guid.NewGuid();
        var unchangedTreeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Changed root"), changedTreeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Bravo"), 1, CancellationToken.None);
        await hierarchy.InsertRootAsync(Node(10, "Unchanged root"), unchangedTreeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(11, "Alpha"), 10, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(12, "Bravo"), 10, CancellationToken.None);
        context.ChangeTracker.Clear();
        var tracked = await context.Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var unchangedBefore = tracked
            .Where(node => node.TreeId == unchangedTreeId)
            .Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position))
            .ToArray();

        tracked.Single(node => node.Id == 2).Name = "Zulu";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        var unchangedAfter = tracked
            .Where(node => node.TreeId == unchangedTreeId)
            .Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position))
            .ToArray();
        var changedOrder = new[] { 3, 2 };
        var unchangedOrder = new[] { 11, 12 };

        Assert.Equal(unchangedBefore, unchangedAfter);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        Assert.Equal(changedOrder, await verification.Set<OrderingNode>()
            .Where(node => node.TreeId == changedTreeId && node.ParentId == 1)
            .OrderBy(node => node.Position)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None));
        Assert.Equal(unchangedOrder, await verification.Set<OrderingNode>()
            .Where(node => node.TreeId == unchangedTreeId && node.ParentId == 10)
            .OrderBy(node => node.Position)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Verifies automatic rename placement preserves the relative order of manually placed unchanged nodes.
    /// </summary>
    [Fact]
    public async Task FlexibleRenamePreservesUnchangedManualOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine, "Flexible");
        var tree = Tree(context);
        await SeedAsync(tree);
        await tree.MoveBeforeAsync(4, 2, CancellationToken.None);
        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Aaron";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(Engine, "Flexible");
        await AssertIdsAsync(Tree(verification).ChildrenOf(1), 3, 4, 2);
        await AssertIdsAsync(Tree(verification).SubtreeOf(3), 3, 5);
        await AssertForestAsync(verification);
    }

    /// <summary>Verifies multiple changed nodes do not reset manual ordering of their unchanged siblings.</summary>
    [Fact]
    public async Task FlexibleMultipleRenamesRetainUnchangedRelativeOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine, "Flexible");
        var tree = Tree(context);
        await SeedAsync(tree);
        await tree.InsertChildAsync(Node(6, "Delta"), 1, CancellationToken.None);
        await tree.MoveBeforeAsync(4, 2, CancellationToken.None);
        var nodes = await context.Set<OrderingNode>()
            .ToDictionaryAsync(node => node.Id, CancellationToken.None);

        nodes[3].Name = "Aaron";
        nodes[6].Name = "Zulu";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(Engine, "Flexible");
        // WHY: Zulu follows its rule predecessor Charlie while Charlie retains its manual position before Alpha.
        await AssertIdsAsync(Tree(verification).ChildrenOf(1), 3, 4, 6, 2);
        await AssertIdsAsync(Tree(verification).ChildrenOf(1).Where(node => node.Id == 4 || node.Id == 2), 4, 2);
        await AssertForestAsync(verification);
    }

    /// <summary>
    ///     Verifies an unrelated payload edit does not silently sort a manually overridden sibling group.
    /// </summary>
    [Fact]
    public async Task PayloadOnlySavePreservesManualPlacementWithoutStructuralUpdates()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine, "Flexible");
        await SeedAsync(Tree(setup));
        await Tree(setup).MoveBeforeAsync(4, 2, CancellationToken.None);
        var probe = new OrderingSaveProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, "Flexible", probe);
        var changed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.Payload = "changed without renaming";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, probe.CompletedPayloadUpdates);
        Assert.Equal(0, probe.StructuralUpdates);
        await using var verification = await _fixture.CreateContextAsync(Engine, "Flexible");
        await AssertIdsAsync(Tree(verification).ChildrenOf(1), 4, 2, 3);
        Assert.Equal("changed without renaming", await verification.Set<OrderingNode>()
            .Where(node => node.Id == 2)
            .Select(node => node.Payload)
            .SingleAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Verifies one rename uses structural projections without constructing all sibling domain entities.
    /// </summary>
    [Fact]
    public async Task RenameDoesNotMaterializeOtherDomainNodes()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var tree = Tree(setup);
        await tree.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);

        for (var index = 2; index <= 33; index++)
        {
            await tree.InsertChildAsync(Node(index, $"Node-{index:D2}"), 1, CancellationToken.None);
        }

        var probe = new OrderingSaveProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        renamed.Name = "Zulu";
        probe.ResetMaterializedNodes();

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Equal(1, probe.CompletedPayloadUpdates);
        Assert.Single(context.ChangeTracker.Entries<OrderingNode>());
        await using var verification = await _fixture.CreateContextAsync(Engine);
        Assert.Equal(Enumerable.Range(3, 31).Append(2), await IdsAsync(Tree(verification).ChildrenOf(1)));
        await AssertForestAsync(verification);
    }
}
