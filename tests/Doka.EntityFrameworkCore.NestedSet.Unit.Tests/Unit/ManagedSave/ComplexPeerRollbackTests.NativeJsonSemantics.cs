using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexPeerRollbackTests
{
    /// <summary>Records EF's native JSON cardinality behavior separately from NestedSet rollback behavior.</summary>
    /// <param name="addItem">Whether to add a raw ordinal or edit an existing raw scalar without detection.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSharedRowSaveDistinguishesRawJsonCardinalityFromScalarEdit(
        bool addItem
    )
    {
        // Arrange
        await using var context = new PeerContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var peer = new ComplexPeer
        {
            Id = 7,
            Items =
            [
                new ComplexItem
                {
                    Text = "Stored one",
                    Tags = [new ComplexTag()],
                },
                new ComplexItem
                {
                    Text = "Stored two",
                    Tags = [new ComplexTag()],
                },
            ],
        };

        var root = new SharingRoot
        {
            Id = 7,
            Peer = peer,
        };

        peer.Root = root;
        await context.AddAsync(root, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        root.Name = "Marked native root";
        context
            .Entry(root)
            .Property(value => value.Name)
            .IsModified = true;

        if (addItem)
        {
            peer.Items.Add(new ComplexItem { Text = "Raw native addition" });
        }
        else
        {
            peer.Items[0].Text = "Raw native edit";
        }

        var peerEntry = context.Entry(peer);
        var peerState = peerEntry.State;
        var collectionModified = peerEntry.ComplexCollection(value => value.Items)
            .IsModified;

        var expectedTexts = addItem
            ? new[] { "Stored one", "Stored two", "Raw native addition" }
            : new[] { "Stored one", "Stored two" };

        TestContext.Current.TestOutputHelper!.WriteLine(
            $"Native EF before save: add={addItem}; peer={peerState}; collectionModified={collectionModified}; "
            + JsonSerializer.Serialize(
                new
                {
                    root.Name,
                    peer.Marker,
                    peer.Optional,
                    peer.Items,
                }));

        // Act
        // WHY: This plain EF model has no NestedSet registration. Shared-row preparation discovers a new
        // JSON ordinal as Added; an existing raw scalar still has no modified flag and remains unsaved.
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        context.ChangeTracker.Clear();
        var stored = await context
            .Set<SharingRoot>()
            .AsNoTracking()
            .Include(value => value.Peer)
            .SingleAsync(CancellationToken.None);

        // Assert
        var storedPeer = Assert.IsType<ComplexPeer>(stored.Peer);
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"Native EF reloaded: items={storedPeer.Items.Count}; "
            + JsonSerializer.Serialize(
                new
                {
                    stored.Name,
                    storedPeer.Marker,
                    storedPeer.Optional,
                    storedPeer.Items,
                }));

        Assert.Equal(EntityState.Unchanged, peerState);
        Assert.False(collectionModified);
        Assert.True(saved > 0);
        Assert.Equal("Marked native root", stored.Name);
        Assert.Equal(expectedTexts, storedPeer.Items.Select(value => value.Text));
        Assert.Equal("Stored marker", storedPeer.Marker);
        Assert.NotNull(storedPeer.Optional);
        Assert.Equal("Stored reference", storedPeer.Optional.Text);
        Assert.Equal("Stored code", storedPeer.Optional.Code);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
