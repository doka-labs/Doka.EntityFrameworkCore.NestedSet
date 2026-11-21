using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SupplementalRelationshipRollbackTests
{
    /// <summary>Preserves a native bucket first initialized after an unrelated raw FK edit.</summary>
    /// <param name="initializeBeforeRawEdit">Whether native membership was indexed before the pending raw edit.</param>
    /// <param name="conceptualNull">Whether a required-FK null flag additionally hides that raw key.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Restore_DivergentRawForeignKey_PreservesActualIndexedBucket(
        bool initializeBeforeRawEdit,
        bool conceptualNull
    )
    {
        // Arrange
        await using var context = conceptualNull
            ? new RequiredContext()
            : new SupplementalContext();

        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
        var original = new Owner
        {
            Id = 2,
            Nodes = [],
        };

        var pending = new Owner
        {
            Id = 1,
            Nodes = [],
        };

        var callback = new Owner
        {
            Id = 3,
            Nodes = [],
        };

        var node = new Node
        {
            Id = 10,
            OwnerId = original.Id,
            Owner = original,
        };

        original.Nodes.Add(node);

        // WHY: Track only the dependent so owner attachment cannot initialize the FK map first. Use the
        // production key lookup: the public update adapter requires a registered principal for this read.
        context.Entry(node)
            .State = EntityState.Unchanged;

        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var update = updates.Entries.Single(entry => ReferenceEquals(
            entry.ToEntityEntry()
                .Entity,
            node));

        var contract = NestedSetInsertionContract.Instance;
        var manager = contract.Manager(update);
        var keys = new[] { pending.Id, original.Id, callback.Id };

        var reference = update.EntityType.FindNavigation(nameof(Node.Owner))!;
        var property = update.EntityType.FindProperty(nameof(Node.OwnerId))!;

        if (initializeBeforeRawEdit)
        {
            _ = contract
                .GetOrCreateDependents(manager, [original.Id], reference.ForeignKey)
                .ToArray();
        }

        node.OwnerId = pending.Id;
        var originalBuckets = keys
            .Select(key => contract
                .GetOrCreateDependents(
                    manager,
                    [key],
                    reference.ForeignKey)
                .ToArray())
            .ToArray();

        if (conceptualNull)
        {
            context
                .Entry(node)
                .Property(value => value.OwnerId)
                .CurrentValue = null;
        }

        var originalModified = update.IsModified(property);

        var originalReference = node.Owner;
        var originalNativeReference = update.GetRelationshipSnapshotValue(reference);
        var originalCollections = new[] { pending, original, callback }
            .Select(owner => owner.Nodes!.ToArray())
            .ToArray();

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        context
            .Entry(node)
            .Property(value => value.OwnerId)
            .CurrentValue = callback.Id;

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Single(
            originalBuckets[initializeBeforeRawEdit
                ? 1
                : 0]);

        Assert.Equal(pending.Id, node.OwnerId);
        Assert.Equal(original.Id, update.GetRelationshipSnapshotValue(property));
        Assert.Same(originalReference, node.Owner);
        Assert.Same(originalNativeReference, update.GetRelationshipSnapshotValue(reference));
        Assert.Equal(originalModified, update.IsModified(property));
        Assert.Equal(conceptualNull, update.IsConceptualNull(property));
        Assert.Equal(
            originalBuckets,
            keys
                .Select(key => contract
                    .GetOrCreateDependents(
                        manager,
                        [key],
                        reference.ForeignKey)
                    .ToArray())
                .ToArray());

        Assert.Equal(
            originalCollections,
            new[] { pending, original, callback }
                .Select(owner => owner.Nodes!.ToArray())
                .ToArray());

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
