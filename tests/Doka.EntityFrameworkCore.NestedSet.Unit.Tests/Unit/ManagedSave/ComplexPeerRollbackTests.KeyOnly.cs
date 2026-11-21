namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexPeerRollbackTests
{
    /// <summary>A peer without a direct non-key scalar still preserves its complex graph storage.</summary>
    /// <param name="empty">Whether the raw graph contains a null reference and replaced empty collection.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ComplexOnlyPeer_NeverNeedsToMarkItsKey(
        bool empty
    )
    {
        // Arrange
        await using var context = new ComplexOnlyPeerContext();
        var entry = Track(context);

        if (empty)
        {
            entry.Entity.Optional = null;
            entry.Entity.Items = [];
        }
        else
        {
            entry.Entity.Items[0].Text = "Raw complex-only edit";
        }

        var instance = entry.Entity.Optional;
        var before = ObserveCollection(entry);
        var update = Update(context, entry.Entity);
        var beforeState = update.EntityState;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        update.EntityState = EntityState.Modified;

        // Act
        snapshot.Restore();

        // Assert
        Assert.All(entry.Metadata.GetProperties(), property => Assert.True(property.IsKey()));
        Assert.Equal(EntityState.Unchanged, beforeState);
        Assert.Equal(EntityState.Unchanged, update.EntityState);
        Assert.Same(instance, entry.Entity.Optional);
        AssertCollection(entry, before);
        Assert.Equal(7, entry.Entity.Id);
        Assert.Equal(
            7,
            entry.Property(value => value.Id)
                .OriginalValue);

        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A required key-only peer returns to Unchanged without ever modifying its shared primary key.</summary>
    [Fact]
    public async Task Restore_KeyOnlyPeer_RestoresWithoutKeyModification()
    {
        // Arrange
        await using var context = new KeyOnlyContext();
        var peer = new KeyOnlyPeer { Id = 7 };
        var root = new SharingRoot
        {
            Id = 7,
            KeyPeer = peer,
        };

        peer.Root = root;
        context.Attach(root);
        root.Name = "Pending root";
        context
            .Entry(root)
            .Property(value => value.Name)
            .IsModified = true;

        var entry = context.Entry(peer);
        var update = Update(context, peer);
        var beforeState = update.EntityState;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        update.EntityState = EntityState.Modified;

        // Act
        snapshot.Restore();

        // Assert
        Assert.All(entry.Metadata.GetProperties(), property => Assert.True(property.IsKey()));
        Assert.Empty(entry.Metadata.GetComplexProperties());
        Assert.Equal(EntityState.Unchanged, beforeState);
        Assert.Equal(EntityState.Unchanged, update.EntityState);
        Assert.Equal(7, peer.Id);
        Assert.Equal(
            7,
            entry.Property(value => value.Id)
                .OriginalValue);

        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);

        Assert.Same(peer, root.KeyPeer);
        Assert.Same(root, peer.Root);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
