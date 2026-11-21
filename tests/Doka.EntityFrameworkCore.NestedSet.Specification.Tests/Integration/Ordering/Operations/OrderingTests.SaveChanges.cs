namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>
    ///     Verifies changing a descending secondary value repositions its subtree among primary-value ties.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_SecondarySortChange_RepositionsTheSubtree()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var tree = Tree(context);
        await tree.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(2, "Same", 10), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(3, "Same", 30), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(4, "Same", 20), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(5, "Leaf"), 2, CancellationToken.None);
        var changed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.Priority = 40;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await AssertIdsAsync(
            Tree(verification)
                .ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            2,
            3,
            4);

        await AssertIdsAsync(
            Tree(verification)
                .SubtreeOf(2),
            cancellationToken: TestContext.Current.CancellationToken,
            2,
            5);

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>Verifies a rename to null follows the configured provider-independent null-last order.</summary>
    [Fact]
    public async Task SaveChangesAsync_NullSortChange_MatchesNativeSiblingOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(Tree(context), cancellationToken: TestContext.Current.CancellationToken);
        var changed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        changed.Name = null;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var tree = Tree(verification);
        var expected = await Nodes(verification)
            .Where(node => node.ParentId == 1)
            .OrderBy(node => node.Name == null
                ? 1
                : 0)
            .ThenBy(node => node.Name)
            .ThenByDescending(node => node.Priority)
            .ThenBy(node => node.Id)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(
            expected,
            await IdsAsync(
                tree.ChildrenOf(1),
                cancellationToken: TestContext.Current.CancellationToken));

        await AssertIdsAsync(tree.SubtreeOf(3), cancellationToken: TestContext.Current.CancellationToken, 3, 5);
        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>Verifies an ordinary rename moves a complete subtree and refreshes every tracked coordinate.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChangesAsync_Rename_RefreshesTrackedSubtreesAndHonorsAcceptance(
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(Tree(context), cancellationToken: TestContext.Current.CancellationToken);
        var tracked = await context
            .Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var renamed = tracked.Single(node => node.Id == 3);
        renamed.Name = "Zulu";

        // Act
        var saved = await context.SaveChangesAsync(acceptAllChangesOnSuccess, CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        var entry = context.Entry(renamed);
        Assert.Equal(
            acceptAllChangesOnSuccess
                ? EntityState.Unchanged
                : EntityState.Modified,
            entry.State);

        Assert.Equal(
            acceptAllChangesOnSuccess
                ? "Zulu"
                : "Bravo",
            entry.Property(node => node.Name)
                .OriginalValue);

        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var persisted = await SnapshotAsync(verification, cancellationToken: TestContext.Current.CancellationToken);
        AssertSnapshot(persisted, tracked);
        await AssertIdsAsync(
            Tree(verification)
                .ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            2,
            4,
            3);

        await AssertIdsAsync(
            Tree(verification)
                .SubtreeOf(3),
            cancellationToken: TestContext.Current.CancellationToken,
            3,
            5);

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>Verifies several pending sort changes are coordinated from their final values in one save.</summary>
    [Fact]
    public async Task SaveChangesAsync_MultipleRenames_ProduceOneConsistentSiblingOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(Tree(context), cancellationToken: TestContext.Current.CancellationToken);
        var nodes = await context
            .Set<OrderingNode>()
            .ToDictionaryAsync(node => node.Id, CancellationToken.None);

        nodes[2].Name = "Zulu";
        nodes[3].Name = "Middle";
        nodes[4].Name = "Aaron";

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, saved);
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await AssertIdsAsync(
            Tree(verification)
                .ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            4,
            3,
            2);

        await AssertIdsAsync(
            Tree(verification)
                .TreeContaining(5),
            cancellationToken: TestContext.Current.CancellationToken,
            1,
            4,
            3,
            5,
            2);

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>Verifies inserting an unrelated row shares the same successful payload and ordering save.</summary>
    [Fact]
    public async Task SaveChangesAsync_Save_PersistsPendingApplicationDataWithTheReorder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(Tree(context), cancellationToken: TestContext.Current.CancellationToken);
        var renamed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";
        await context.AddAsync(
            new OrderingMarker
            {
                Id = 1,
                Value = "domain event",
            },
            CancellationToken.None);

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, saved);
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        Assert.Equal(
            "domain event",
            await verification
                .Set<OrderingMarker>()
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));

        await AssertIdsAsync(
            Tree(verification)
                .ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            2,
            4,
            3);

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>
    ///     Verifies one rename uses structural projections without constructing all sibling domain entities.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_Rename_DoesNotMaterializeOtherDomainNodes()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var tree = Tree(setup);
        await tree.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);

        for (var index = 2; index <= 33; index++)
        {
            await tree.InsertChildAsync(Node(index, $"Node-{index:D2}"), 1, CancellationToken.None);
        }

        var probe = new OrderingSaveProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict",
            interceptors: [probe]);

        var renamed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        renamed.Name = "Zulu";
        probe.ResetMaterializedNodes();

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Equal(1, probe.CompletedPayloadUpdates);
        Assert.Single(context.ChangeTracker.Entries<OrderingNode>());
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        Assert.Equal(
            Enumerable
                .Range(3, 31)
                .Append(2),
            await IdsAsync(
                Tree(verification)
                    .ChildrenOf(1),
                cancellationToken: TestContext.Current.CancellationToken));

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }
}
