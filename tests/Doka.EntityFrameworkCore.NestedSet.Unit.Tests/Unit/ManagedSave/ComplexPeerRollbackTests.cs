using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves raw complex graphs through real flagless table-sharing state promotion.</summary>
public sealed partial class ComplexPeerRollbackTests
{
    /// <summary>Raw complex collection edits preserve exact list, element, original and state identities.</summary>
    /// <param name="change">The caller's raw collection mutation before snapshot capture.</param>
    /// <param name="mode">Plain no-promotion control, promoted sharing peer or already Modified sharing peer.</param>
    [Theory]
    [InlineData("edit", "plain")]
    [InlineData("add", "plain")]
    [InlineData("remove", "plain")]
    [InlineData("replace", "plain")]
    [InlineData("edit", "promoted")]
    [InlineData("add", "promoted")]
    [InlineData("remove", "promoted")]
    [InlineData("replace", "promoted")]
    [InlineData("edit", "modified")]
    [InlineData("add", "modified")]
    [InlineData("remove", "modified")]
    [InlineData("replace", "modified")]
    public async Task RawComplexCollectionPreservesGraphAndState(
        string change,
        string mode
    )
    {
        // Arrange
        await using var context = mode == "plain"
            ? new PlainPeerContext()
            : new PeerContext();

        var entry = Track(context);
        var peer = entry.Entity;
        ChangeCollection(peer, change);

        if (mode == "modified")
        {
            entry.Property(value => value.Marker)
                .IsModified = true;
        }

        var update = Update(context, peer);
        var beforeState = update.EntityState;
        var markerOriginal = entry.Property(value => value.Marker)
            .OriginalValue;

        var markerModified = entry.Property(value => value.Marker)
            .IsModified;

        var collectionBefore = ObserveCollection(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        if (mode != "plain")
        {
            // WHY: EF promotes the actual sharing peer without marking scalar properties. No callback
            // mutation after capture is needed; whole-entity rejection itself must not replace this raw graph.
            update.EntityState = EntityState.Modified;
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(
            mode == "modified"
                ? EntityState.Modified
                : EntityState.Unchanged,
            beforeState);

        Assert.Equal(beforeState, update.EntityState);
        AssertCollection(entry, collectionBefore);
        Assert.Equal("Pending marker", peer.Marker);
        Assert.Equal(
            markerOriginal,
            entry.Property(value => value.Marker)
                .OriginalValue);

        Assert.Equal(
            markerModified,
            entry.Property(value => value.Marker)
                .IsModified);

        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);

        Assert.Equal(7, peer.Id);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Optional complex edits, replacements and literal null retain independent originals and flags.</summary>
    /// <param name="change">The raw complex reference mutation before snapshot capture.</param>
    /// <param name="mode">Plain no-promotion, promoted sharing or already Modified sharing control.</param>
    [Theory]
    [InlineData("edit", "plain")]
    [InlineData("replace", "plain")]
    [InlineData("null", "plain")]
    [InlineData("edit", "promoted")]
    [InlineData("replace", "promoted")]
    [InlineData("null", "promoted")]
    [InlineData("edit", "modified")]
    [InlineData("replace", "modified")]
    [InlineData("null", "modified")]
    public async Task OptionalComplexPreservesInstanceNullAndOriginals(
        string change,
        string mode
    )
    {
        // Arrange
        await using var context = mode == "plain"
            ? new PlainPeerContext()
            : new PeerContext();

        var entry = Track(context);
        var peer = entry.Entity;

        switch (change)
        {
            case "edit":
                peer.Optional!.Text = "Raw reference";
                break;
            case "replace":
                peer.Optional = new ComplexReference
                {
                    Text = "Replacement reference",
                    Code = "Replacement code",
                };

                break;
            case "null":
                peer.Optional = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        if (mode == "modified")
        {
            entry.Property(value => value.Marker)
                .IsModified = true;
        }

        var update = Update(context, peer);
        var beforeState = update.EntityState;
        var instance = peer.Optional;
        var text = instance?.Text;
        var code = instance?.Code;
        var reference = entry.ComplexProperty(value => value.Optional);
        var modified = reference.IsModified;
        var originals = reference
            .Properties
            .Select(property => (property.Metadata.Name, property.OriginalValue))
            .ToArray();

        var collectionBefore = ObserveCollection(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        if (mode != "plain")
        {
            update.EntityState = EntityState.Modified;
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(
            mode == "modified"
                ? EntityState.Modified
                : EntityState.Unchanged,
            beforeState);

        Assert.Equal(beforeState, update.EntityState);
        Assert.Same(instance, peer.Optional);
        Assert.Equal(text, peer.Optional?.Text);
        Assert.Equal(code, peer.Optional?.Code);
        Assert.Equal(modified, reference.IsModified);
        Assert.Equal(
            originals,
            reference.Properties.Select(property => (property.Metadata.Name, property.OriginalValue)));

        AssertCollection(entry, collectionBefore);
        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);

        Assert.Equal(7, peer.Id);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>An originally Modified complex collection retains its independent modification flags.</summary>
    [Fact]
    public async Task OriginallyModifiedComplexCollectionKeepsModificationIntent()
    {
        // Arrange
        await using var context = new PeerContext();
        var entry = Track(context);
        entry.Entity.Items[0].Text = "Intended complex write";
        var collection = entry.ComplexCollection(value => value.Items);
        collection.IsModified = true;
        var before = ObserveCollection(entry);
        var update = Update(context, entry.Entity);
        var beforeState = update.EntityState;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        update.EntityState = EntityState.Modified;

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Modified, beforeState);
        Assert.True(before.Modified);
        Assert.Equal(EntityState.Modified, update.EntityState);
        AssertCollection(entry, before);
        Assert.True(collection.IsModified);
        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A peer without a direct non-key scalar still preserves its complex graph storage.</summary>
    /// <param name="empty">Whether the raw graph contains a null reference and replaced empty collection.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComplexOnlyPeerNeverNeedsToMarkItsKey(
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

    /// <summary>An outer element state change cannot reject its pending nested collection graph.</summary>
    /// <param name="promoteOuter">Whether a callback marks the actual outer element Modified after capture.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OuterElementPromotionPreservesNestedCollection(
        bool promoteOuter
    )
    {
        // Arrange
        await using var context = new PeerContext();
        var entry = Track(context);
        var peer = entry.Entity;
        peer
            .Items[0]
            .Tags[0].Text = "Raw nested edit";

        peer
            .Items[0]
            .Tags
            .Add(new ComplexTag { Text = "Raw nested added" });

        var outer = entry
            .ComplexCollection(value => value.Items)[0];

        var nested = outer.ComplexCollection(nameof(ComplexItem.Tags));
        var tags = peer.Items[0].Tags;
        var tagObservations = Enumerable
            .Range(0, tags.Count)
            .Select(index =>
            {
                var element = nested[index];
                var property = element.Property(nameof(ComplexTag.Text));

                return (Instance: tags[index], element.State, Current: property.CurrentValue,
                    Original: property.OriginalValue, Modified: property.IsModified);
            })
            .ToArray();

        var nestedModified = nested.IsModified;
        var collectionBefore = ObserveCollection(entry);
        var update = Update(context, peer);
        var beforeState = update.EntityState;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        if (promoteOuter)
        {
            // WHY: The public element state setter can affect nested state independently of the peer's
            // promotion. Restoring that outer state must not reject a valid pre-capture nested raw graph.
            outer.State = EntityState.Modified;
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Unchanged, beforeState);
        Assert.Equal(beforeState, update.EntityState);
        AssertCollection(entry, collectionBefore);
        Assert.Same(tags, peer.Items[0].Tags);
        Assert.Equal(nestedModified, nested.IsModified);
        Assert.Equal(tagObservations.Length, tags.Count);

        // WHY: Nested CLR references and native element/property slots must retain the same ordinal pairing.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < tagObservations.Length; index++)
        {
            var expected = tagObservations[index];
            var element = nested[index];
            var property = element.Property(nameof(ComplexTag.Text));

            Assert.Same(expected.Instance, tags[index]);
            Assert.Equal(expected.State, element.State);
            Assert.Equal(expected.Current, property.CurrentValue);
            Assert.Equal(expected.Original, property.OriginalValue);
            Assert.Equal(expected.Modified, property.IsModified);
        }

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
    public async Task KeyOnlyPeerRestoresWithoutKeyModification()
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

    /// <summary>Tracks a valid loaded graph with stored originals before any raw complex mutation.</summary>
    private static EntityEntry<ComplexPeer> Track(
        PeerContext context
    )
    {
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

        if (context is PlainPeerContext)
        {
            context.Attach(peer);
        }
        else
        {
            var root = new SharingRoot
            {
                Id = peer.Id,
                Peer = peer,
            };

            peer.Root = root;
            context.Attach(root);
            root.Name = "Pending root";
            context
                .Entry(root)
                .Property(value => value.Name)
                .IsModified = true;
        }

        _ = context.Model.GetRelationalModel();
        peer.Marker = "Pending marker";

        return context.Entry(peer);
    }

    /// <summary>Changes only CLR storage while automatic detection remains disabled.</summary>
    private static void ChangeCollection(
        ComplexPeer peer,
        string change
    )
    {
        switch (change)
        {
            case "edit":
                peer.Items[0].Text = "Raw edit";
                break;
            case "add":
                peer.Items.Add(new ComplexItem { Text = "Raw added" });
                break;
            case "remove":
                peer.Items.RemoveAt(0);
                break;
            case "replace":
                peer.Items = [new ComplexItem { Text = "Raw replacement" }];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    /// <summary>Obtains actual provider state without requesting global change detection.</summary>
    private static IUpdateEntry Update(
        DbContext context,
        object entity
    )
        => context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Single(entry => ReferenceEquals(
                entry.ToEntityEntry()
                    .Entity,
                entity));

    /// <summary>Captures observable element references and independent current/original/flag/state storage.</summary>
    private static CollectionObservation ObserveCollection(
        EntityEntry<ComplexPeer> entry
    )
    {
        var collection = entry.ComplexCollection(value => value.Items);
        var values = entry.Entity.Items;
        var elements = new ElementObservation[values.Count];

        // WHY: CLR elements and actual EF element entries must be observed at the same collection ordinal.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < values.Count; index++)
        {
            var element = collection[index];
            var property = element.Property(nameof(ComplexItem.Text));
            elements[index] = new ElementObservation(
                values[index],
                element.State,
                property.CurrentValue,
                property.OriginalValue,
                property.IsModified,
                values[index].Tags);
        }

        return new CollectionObservation(values, collection.IsModified, elements);
    }

    /// <summary>Checks identities, current/original values, flags and element states after rollback.</summary>
    private static void AssertCollection(
        EntityEntry<ComplexPeer> entry,
        CollectionObservation before
    )
    {
        Assert.Same(before.Instance, entry.Entity.Items);
        Assert.Equal(before.Elements.Length, entry.Entity.Items.Count);
        var collection = entry.ComplexCollection(value => value.Items);

        Assert.Equal(before.Modified, collection.IsModified);

        // WHY: Exact ordinal assertions reject a replacement list or cloned elements with equal content.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Elements.Length; index++)
        {
            var expected = before.Elements[index];
            var element = collection[index];
            var property = element.Property(nameof(ComplexItem.Text));

            Assert.Same(expected.Instance, entry.Entity.Items[index]);
            Assert.Same(expected.Tags, entry.Entity.Items[index].Tags);
            Assert.Equal(expected.State, element.State);
            Assert.Equal(expected.Current, property.CurrentValue);
            Assert.Equal(expected.Original, property.OriginalValue);
            Assert.Equal(expected.Modified, property.IsModified);
        }
    }

    /// <summary>Retains test observations without introducing any alternate EF entry implementation.</summary>
    private sealed record CollectionObservation(
        List<ComplexItem> Instance,
        bool Modified,
        ElementObservation[] Elements
    );

    /// <summary>Pairs one actual complex element with its independently observed public EF state.</summary>
    private sealed record ElementObservation(
        ComplexItem Instance,
        EntityState State,
        object? Current,
        object? Original,
        bool Modified,
        List<ComplexTag> Tags
    );
}
