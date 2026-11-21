using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CollectionBaselineOwnershipTests
{
    /// <summary>Known detached inputs preserve notification collections without native collection snapshots.</summary>
    [Fact]
    public async Task CaptureInputOwners_InputOwnerRollback_PreservesNotificationCollectionWithoutRetrackingKnownInput()
    {
        // Arrange
        await using var context = new NotificationBaselineContext();
        var owner = new NotificationOwner { Id = 7 };
        var dependent = new NotificationDependent
        {
            Id = 11,
            OwnerId = owner.Id,
            Owner = owner,
        };

        owner.Dependents!.Add(dependent);
        var ownerEntry = context.Attach(owner);
        var entry = context.Entry(dependent);
        var trackedStateBefore = entry.State;

        // WHY: Known future inputs may remain in a prepared CLR collection after detachment. This owner
        // uses notifications, so preserving that baseline must not read an EF collection snapshot.
        entry.State = EntityState.Detached;
        dependent.Owner = null;
        dependent.OwnerId = null;
        owner.Payload = "Pending application payload";
        var update = NestedSetInsertionContract.Instance.Entry(entry);
        var ownerUpdate = NestedSetInsertionContract.Instance.Entry(ownerEntry);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var foreignKey = entry
            .Metadata
            .GetForeignKeys()
            .Single();

        var navigation = foreignKey.PrincipalToDependent!;
        var collection = owner.Dependents!;
        var containedBefore = collection.Contains(dependent);
        var detachedStateBefore = entry.State;
        var inputs = new NestedSetImportInputs(
            (input, key, principal) => ReferenceEquals(input, update)
                && ReferenceEquals(key, foreignKey)
                && ReferenceEquals(principal, owner),
            input => ReferenceEquals(input, dependent)
                ? entry.Metadata
                : null,
            [foreignKey],
            entry.Metadata);

        NestedSetCollectionBaseline? journal = null;

        // Act
        NestedSetCollectionBaseline.CaptureInputOwners(context, inputs, ref journal);
        var removedDuringCallback = collection.Remove(dependent);
        var stateAfterCallback = entry.State;
        journal?.Restore();

        // Assert
        Assert.Equal(
            ChangeTrackingStrategy.ChangingAndChangedNotifications,
            ownerEntry.Metadata.GetChangeTrackingStrategy());

        Assert.Equal(nameof(NotificationOwner.Dependents), navigation.Name);
        Assert.Equal(EntityState.Unchanged, trackedStateBefore);
        Assert.Equal(EntityState.Detached, detachedStateBefore);
        Assert.True(containedBefore);
        Assert.True(removedDuringCallback);
        Assert.Equal(EntityState.Detached, stateAfterCallback);
        Assert.NotNull(journal);
        Assert.Same(collection, owner.Dependents);
        Assert.Same(dependent, Assert.Single(collection));
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Null(updates.FindPrincipal(update, foreignKey));
        Assert.Empty(updates.GetDependents(ownerUpdate, foreignKey));
        Assert.DoesNotContain(updates.Entries, tracked => ReferenceEquals(tracked, update));
        Assert.Equal(EntityState.Modified, ownerEntry.State);
        Assert.True(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            ownerEntry.Property(value => value.Id)
                .IsModified);

        Assert.Equal("Pending application payload", owner.Payload);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Rollback preserves nullable collections and resumes only EF's own notification handlers.</summary>
    /// <param name="strategy">The actual notification strategy used by this model.</param>
    /// <param name="initiallyNull">Whether the pre-operation owner collection is literally null.</param>
    [Theory]
    [InlineData(ChangeTrackingStrategy.ChangedNotifications, false)]
    [InlineData(ChangeTrackingStrategy.ChangedNotifications, true)]
    [InlineData(ChangeTrackingStrategy.ChangingAndChangedNotifications, false)]
    [InlineData(ChangeTrackingStrategy.ChangingAndChangedNotifications, true)]
    [InlineData(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues, false)]
    [InlineData(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues, true)]
    public async Task CaptureBeforeTracking_NotificationRollback_PreservesCollectionAndResumesSubscriptions(
        ChangeTrackingStrategy strategy,
        bool initiallyNull
    )
    {
        // Arrange
        await using var context = strategy switch
        {
            ChangeTrackingStrategy.ChangedNotifications => new ChangedNotificationBaselineContext(),
            ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues =>
                new OriginalValueNotificationBaselineContext(),
            _ => new NotificationBaselineContext(),
        };

        var owner = new NotificationOwner { Id = 7 };
        var dependent = new NotificationDependent
        {
            Id = 11,
            OwnerId = owner.Id,
            Owner = owner,
        };

        var original = owner.Dependents!;
        original.Add(dependent);
        var ownerEntry = context.Attach(owner);
        var entry = context.Entry(dependent);
        entry.State = EntityState.Detached;
        dependent.Owner = null;
        dependent.OwnerId = initiallyNull
            ? owner.Id
            : null;

        if (initiallyNull)
        {
            // WHY: CaptureBeforeTracking can resolve a real principal from the detached input's FK even
            // when the caller intentionally cleared its collection after ordinary tracking began.
            owner.Dependents = null;
        }

        owner.Payload = "Pending application payload";
        var update = NestedSetInsertionContract.Instance.Entry(entry);
        var ownerUpdate = NestedSetInsertionContract.Instance.Entry(ownerEntry);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var key = entry
            .Metadata
            .GetForeignKeys()
            .Single();

        var principalBefore = updates.FindPrincipal(update, key);
        var inputs = new NestedSetImportInputs(
            (input, foreignKey, principal) => ReferenceEquals(input, update)
                && ReferenceEquals(foreignKey, key)
                && ReferenceEquals(principal, owner),
            input => ReferenceEquals(input, dependent)
                ? entry.Metadata
                : null,
            [key],
            entry.Metadata);

        var replacement = new NotificationDependents();
        var originalNativeHandlers = original.NativeHandlerCount;
        var replacementNativeHandlers = replacement.NativeHandlerCount;
        var applicationCollections = 0;
        var applicationProperties = 0;
        original.CollectionChanged += (_, _) => applicationCollections++;
        owner.PropertyChanged += (_, _) => applicationProperties++;
        NestedSetCollectionBaseline? journal = null;

        // Act
        if (initiallyNull)
        {
            NestedSetCollectionBaseline.CaptureBeforeTracking(entry, ref journal);
            entry.State = EntityState.Added;
            entry.State = EntityState.Detached;
        }
        else
        {
            NestedSetCollectionBaseline.CaptureInputOwners(context, inputs, ref journal);
            original.Remove(dependent);
        }

        owner.Dependents = replacement;
        journal?.Restore();
        var restored = owner.Dependents;
        var restoredMembers = restored?.ToArray();
        var restoredState = ownerEntry.State;
        var restoredPayloadModified = ownerEntry.Property(value => value.Payload)
            .IsModified;

        var restoredPayload = owner.Payload;
        var finalOriginalNativeHandlers = original.NativeHandlerCount;
        var finalReplacementNativeHandlers = replacement.NativeHandlerCount;
        var applicationPropertiesBeforeProbe = applicationProperties;
        ownerEntry.Property(value => value.Payload)
            .IsModified = false;

        owner.Payload = "Notification after rollback";

        // Assert
        Assert.Equal(strategy, ownerEntry.Metadata.GetChangeTrackingStrategy());
        Assert.NotNull(journal);

        if (initiallyNull)
        {
            Assert.Same(ownerUpdate, principalBefore);
            Assert.Null(restored);
            Assert.Null(restoredMembers);
        }
        else
        {
            Assert.Null(principalBefore);
            Assert.Equal(1, originalNativeHandlers);
            Assert.Same(original, restored);
            Assert.Same(dependent, Assert.Single(restoredMembers!));
            Assert.True(applicationCollections >= 2);
        }

        Assert.Equal(originalNativeHandlers, finalOriginalNativeHandlers);
        Assert.Equal(0, replacementNativeHandlers);
        Assert.Equal(replacementNativeHandlers, finalReplacementNativeHandlers);
        Assert.Equal(EntityState.Modified, restoredState);
        Assert.True(restoredPayloadModified);
        Assert.Equal("Pending application payload", restoredPayload);
        Assert.Equal(applicationPropertiesBeforeProbe + 1, applicationProperties);
        Assert.Equal(EntityState.Modified, ownerEntry.State);
        Assert.True(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            ownerEntry.Property(value => value.Id)
                .IsModified);

        Assert.Equal(EntityState.Detached, entry.State);
        Assert.DoesNotContain(updates.Entries, tracked => ReferenceEquals(tracked, update));
        Assert.Empty(updates.GetDependents(ownerUpdate, key));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
