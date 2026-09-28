namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTrackerTests
{
    /// <summary>Matches EF's unaccepted token state while accepting bulk-refreshed unchanged siblings.</summary>
    [Fact]
    public async Task SaveWithoutAcceptancePreservesGeneratedTokenSemanticsAndUnchangedSiblings()
    {
        // Arrange
        var database = await CreateDatabaseAsync(Engine);
        await using var context = new TrackerContext(await OptionsAsync(database));
        await using var plain = new PlainTrackerContext(await OptionsAsync(database));
        var nodes = await context
            .Set<TrackerNode>()
            .ToDictionaryAsync(node => node.Id, CancellationToken.None);

        var plainNode = await LoadNodeAsync(plain, 3);
        nodes[3].Name = "Zulu";
        plainNode.Name = "Zulu";
        var siblingPositionBefore = nodes[4].Position;
        var siblingTokenBefore = nodes[4].CoordinateToken;

        // Act
        await plain.SaveChangesAsync(false, CancellationToken.None);
        var plainSnapshot = Capture(plain, plainNode);
        await context.SaveChangesAsync(false, CancellationToken.None);
        var changedSnapshot = Capture(context, nodes[3]);
        var siblingSnapshot = Capture(context, nodes[4]);
        await using var verification = new TrackerContext(await OptionsAsync(database));
        var persisted = await verification
            .Set<TrackerNode>()
            .AsNoTracking()
            .ToDictionaryAsync(node => node.Id, CancellationToken.None);

        var order = await ChildIdsAsync(verification);

        // Assert
        Assert.Equal(EntityState.Modified, changedSnapshot.State);
        Assert.Equal(plainSnapshot.State, changedSnapshot.State);
        Assert.Equal(
            plainSnapshot.Properties.Single(property => property.Name == "Name"),
            changedSnapshot.Properties.Single(property => property.Name == "Name"));
        Assert.Equal(
            plainSnapshot.Properties.Single(property => property.Name == "NameToken"),
            changedSnapshot.Properties.Single(property => property.Name == "NameToken"));

        var pendingToken = changedSnapshot.Properties.Single(property => property.Name == "CoordinateToken");
        var plainToken = plainSnapshot.Properties.Single(property => property.Name == "CoordinateToken");
        Assert.Equal(plainToken.Original, pendingToken.Original);
        Assert.Equal(plainToken.IsModified, pendingToken.IsModified);
        Assert.Equal(plainToken.IsTemporary, pendingToken.IsTemporary);
        Assert.Equal(persisted[3].CoordinateToken, pendingToken.Current);
        Assert.Equal(EntityState.Unchanged, siblingSnapshot.State);
        Assert.NotEqual(siblingPositionBefore, persisted[4].Position);
        Assert.NotEqual(siblingTokenBefore, persisted[4].CoordinateToken);
        Assert.Equal(persisted[4].Position, nodes[4].Position);
        Assert.Equal(persisted[4].CoordinateToken, nodes[4].CoordinateToken);
        Assert.All(
            siblingSnapshot.Properties,
            property =>
            {
                Assert.False(property.IsModified);
                Assert.False(property.IsTemporary);
                Assert.Equal(property.Current, property.Original);
            });
        Assert.Equal([2, 4, 3], order);
    }

    /// <summary>Refreshes generated tokens at the final insertion position before later attachment.</summary>
    [Fact]
    public async Task OrderedInsertRefreshesGeneratedTokenBeforeAnAttachedPayloadSave()
    {
        // Arrange
        var database = await CreateDatabaseAsync(Engine);
        await using var context = new TrackerContext(await OptionsAsync(database));
        var node = Node(10, "Alpha root");

        // Act
        await Tree(context)
            .InsertRootAsync(node, Guid.NewGuid(), CancellationToken.None);

        var returnedToken = node.CoordinateToken;
        var returnedPosition = node.Position;

        await using var verification = new TrackerContext(await OptionsAsync(database));
        var inserted = await verification
            .Set<TrackerNode>()
            .AsNoTracking()
            .SingleAsync(value => value.Id == node.Id, CancellationToken.None);

        context.Attach(node);
        node.Name = "Zulu root";
        var saveFailure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var roots = await verification
            .Set<TrackerNode>()
            .AsNoTracking()
            .Where(value => value.Scope == 1 && value.ParentId == null)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        var persisted = await verification
            .Set<TrackerNode>()
            .AsNoTracking()
            .SingleAsync(value => value.Id == node.Id, CancellationToken.None);

        // Assert
        Assert.Equal(0, returnedPosition);
        Assert.Equal(1, node.Left);
        Assert.Equal(2, node.Right);
        Assert.Equal(inserted.CoordinateToken, returnedToken);
        Assert.Null(saveFailure);
        Assert.Equal([1, 10], roots);
        Assert.NotEqual(
            inserted.TreeId,
            await verification
                .Set<TrackerNode>()
                .Where(value => value.Id == 1)
                .Select(value => value.TreeId)
                .SingleAsync(CancellationToken.None));
        Assert.Equal("Zulu root", persisted.Name);
        Assert.Equal(persisted.CoordinateToken, node.CoordinateToken);
        Assert.Equal(EntityState.Unchanged, Capture(context, node).State);
    }
}
