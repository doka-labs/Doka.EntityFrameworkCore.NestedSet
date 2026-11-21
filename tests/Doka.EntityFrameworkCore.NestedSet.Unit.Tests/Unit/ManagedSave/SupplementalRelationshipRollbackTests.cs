using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Separates native relationship recovery from unrelated pending CLR container changes.</summary>
public sealed partial class SupplementalRelationshipRollbackTests
{
    /// <summary>A null preimage must not discard unrelated raw members introduced into a new collection.</summary>
    /// <param name="pendingMember">Whether an unrelated raw member requires retaining the new container.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_NullCollection_PreservesOnlyUnrelatedPendingMembers(
        bool pendingMember
    )
    {
        // Arrange
        await using var context = new SupplementalContext();
        var original = new Owner
        {
            Id = 2,
            Nodes = [],
        };

        var temporary = new Owner
        {
            Id = 1,
            Nodes = [],
        };

        var node = new Node
        {
            Id = 10,
            OwnerId = 2,
            Owner = original,
        };

        var raw = new Node { Id = 20 };
        original.Nodes.Add(node);
        context.AttachRange(original, temporary, raw);

        // WHY: The raw-null CLR collection and installed native membership are independent before capture.
        original.Nodes = null;
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var update = updates.Entries.Single(entry => ReferenceEquals(
            entry.ToEntityEntry()
                .Entity,
            node));

        var owner = updates.Entries.Single(entry => ReferenceEquals(
            entry.ToEntityEntry()
                .Entity,
            original));

        var navigation = owner.EntityType.FindNavigation(nameof(Owner.Nodes))!;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        context
            .Entry(node)
            .Property(value => value.OwnerId)
            .CurrentValue = temporary.Id;

        node.OwnerId = original.Id;
        context
            .Entry(node)
            .Property(value => value.OwnerId)
            .IsModified = true;

        var replacement = new List<Node> { node };

        if (pendingMember)
        {
            replacement.Add(raw);
        }

        original.Nodes = replacement;
        snapshot.RecordPersistedAddition(owner, navigation, node);
        NestedSetInsertionContract.Instance.AddCollectionSnapshot(owner, navigation, node);

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(2, node.OwnerId);
        Assert.Same(original, node.Owner);
        Assert.Contains(node, Assert.IsAssignableFrom<ISet<object>>(owner.GetRelationshipSnapshotValue(navigation)));
        Assert.False(update.IsModified(update.EntityType.FindProperty(nameof(Node.OwnerId))!));
        Assert.Empty(temporary.Nodes!);

        if (pendingMember)
        {
            Assert.Same(replacement, original.Nodes);
            Assert.Same(raw, Assert.Single(original.Nodes!));
            Assert.Null(raw.OwnerId);
            Assert.Null(raw.Owner);
            Assert.DoesNotContain(
                raw,
                Assert.IsAssignableFrom<ISet<object>>(owner.GetRelationshipSnapshotValue(navigation)));
        }
        else
        {
            Assert.Null(original.Nodes);
        }

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Native presence survives an unmarked round trip while its raw CLR reorder remains pending.</summary>
    /// <param name="originalMembership">Whether EF originally included the middle member in its native set.</param>
    /// <param name="ownedReceipt">Whether provider completion installed a library-owned inverse addition.</param>
    /// <param name="marked">Whether the callback retains explicit FK write intent after the round trip.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Restore_RoundTrip_PreservesNativePresenceAndIndependentRawOrder(
        bool originalMembership,
        bool ownedReceipt,
        bool marked
    )
    {
        // Arrange
        await using var context = new SupplementalContext();
        var original = new Owner
        {
            Id = 2,
            Nodes = [],
        };

        var transient = new Owner
        {
            Id = 1,
            Nodes = [],
        };

        var first = new Node
        {
            Id = 10,
            OwnerId = original.Id,
            Owner = original,
        };

        var middle = new Node
        {
            Id = 20,
            OwnerId = original.Id,
            Owner = original,
        };

        var last = new Node
        {
            Id = 30,
            OwnerId = original.Id,
            Owner = original,
        };

        original.Nodes.AddRange([first, last]);
        context.AttachRange(original, transient);

        if (originalMembership)
        {
            context.Attach(middle);
            original.Nodes.Remove(middle);
            original.Nodes.Insert(1, middle);
        }
        else
        {
            // WHY: Preparing CLR membership before EF attaches the dependent leaves its native membership absent.
            original.Nodes.Insert(1, middle);
            context.Attach(middle);
        }

        var contract = NestedSetInsertionContract.Instance;
        var update = contract.Entry(context.Entry(middle));
        var owner = contract.Entry(context.Entry(original));
        var navigation = owner.EntityType.FindNavigation(nameof(Owner.Nodes))!;
        var nativeBefore = Assert
            .IsAssignableFrom<ISet<object>>(owner.GetRelationshipSnapshotValue(navigation))
            .ToArray();

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        original.Nodes.Add(middle);
        var property = context
            .Entry(middle)
            .Property(node => node.OwnerId);

        property.CurrentValue = transient.Id;
        property.CurrentValue = original.Id;
        property.IsModified = marked;
        var pendingOrder = original.Nodes.ToArray();

        if (ownedReceipt)
        {
            snapshot.RecordPersistedAddition(owner, navigation, middle);
            contract.AddCollectionSnapshot(owner, navigation, middle);
        }

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(originalMembership, nativeBefore.Contains(middle, ReferenceEqualityComparer.Instance));
        Assert.Equal(new[] { first, last, middle }, pendingOrder);
        Assert.Equal(
            marked
                ? new[] { first, middle, last }
                : pendingOrder,
            original.Nodes);

        Assert.True(
            Assert
                .IsAssignableFrom<ISet<object>>(owner.GetRelationshipSnapshotValue(navigation))
                .SetEquals(nativeBefore));

        Assert.Equal(original.Id, middle.OwnerId);
        Assert.Same(original, middle.Owner);
        Assert.Same(
            original,
            update.GetRelationshipSnapshotValue(
                update.EntityType.FindNavigation(nameof(Node.Owner))!));

        Assert.False(property.IsModified);
        Assert.Empty(transient.Nodes!);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
