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
    public async Task Restore_RawComplexCollection_PreservesGraphAndState(
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
    public async Task Restore_OptionalComplex_PreservesInstanceNullAndOriginals(
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
    public async Task Restore_OriginallyModifiedComplexCollection_KeepsModificationIntent()
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

    /// <summary>An outer element state change cannot reject its pending nested collection graph.</summary>
    /// <param name="promoteOuter">Whether a callback marks the actual outer element Modified after capture.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_OuterElementPromotion_PreservesNestedCollection(
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
}
