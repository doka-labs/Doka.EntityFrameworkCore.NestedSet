using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Keeps external collection journals independent of introduced roots and their owned payload rows.</summary>
public sealed partial class CollectionBaselineOwnershipTests
{
    /// <summary>Owned payload rows do not create retained external collection receipts.</summary>
    /// <param name="rootState">The actual tracked ownership principal state before the payload is introduced.</param>
    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Unchanged)]
    public async Task CaptureBeforeTracking_BeforeTracking_IgnoresOwnership(
        EntityState rootState
    )
    {
        // Arrange
        await using var context = new BaselineContext();
        var root = new OwnedRoot { Id = 1 };
        var rootEntry = context.Entry(root);
        rootEntry.State = rootState;
        var payload = new OwnedPayload
        {
            Id = 11,
            RootId = root.Id,
            Root = root,
        };

        var entry = context.Entry(payload);
        var update = NestedSetInsertionContract.Instance.Entry(entry);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var ownership = entry
            .Metadata
            .GetForeignKeys()
            .Single(key => key.IsOwnership);

        var principalBefore = updates.FindPrincipal(update, ownership);
        var principalStateBefore = principalBefore?.EntityState;
        var payloadStateBefore = entry.State;
        NestedSetCollectionBaseline? journal = null;

        // Act
        NestedSetCollectionBaseline.CaptureBeforeTracking(entry, ref journal);

        // Assert
        Assert.Same(NestedSetInsertionContract.Instance.Entry(rootEntry), principalBefore);
        Assert.Equal(rootState, principalStateBefore);
        Assert.Equal(EntityState.Detached, payloadStateBefore);
        Assert.Equal(rootState, rootEntry.State);
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Null(journal);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>An introduced ordinary principal does not create a retained external collection receipt.</summary>
    [Fact]
    public async Task CaptureBeforeTracking_BeforeTracking_IgnoresAddedExternalPrincipal()
    {
        // Arrange
        await using var context = new BaselineContext();
        var owner = new ExternalOwner { Id = 7 };
        var ownerEntry = context.Add(owner);
        var dependent = new ExternalDependent
        {
            Id = 21,
            ExternalOwnerId = owner.Id,
            ExternalOwner = owner,
        };

        var entry = context.Entry(dependent);
        var update = NestedSetInsertionContract.Instance.Entry(entry);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var key = entry
            .Metadata
            .GetForeignKeys()
            .Single();

        var principalBefore = updates.FindPrincipal(update, key);
        var principalStateBefore = principalBefore?.EntityState;
        var dependentStateBefore = entry.State;
        NestedSetCollectionBaseline? journal = null;

        // Act
        NestedSetCollectionBaseline.CaptureBeforeTracking(entry, ref journal);

        // Assert
        Assert.Same(NestedSetInsertionContract.Instance.Entry(ownerEntry), principalBefore);
        Assert.Equal(EntityState.Added, principalStateBefore);
        Assert.Equal(EntityState.Detached, dependentStateBefore);
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.False(key.IsOwnership);
        Assert.Null(journal);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>An existing external owner retains CLR order and native baseline after input cleanup.</summary>
    [Fact]
    public async Task CaptureBeforeTracking_BeforeTracking_PreservesExistingExternalOwnerBaseline()
    {
        // Arrange
        await using var context = new BaselineContext();
        var owner = new ExternalOwner { Id = 7 };
        var first = new ExternalDependent
        {
            Id = 11,
            ExternalOwnerId = owner.Id,
            ExternalOwner = owner,
        };

        var second = new ExternalDependent
        {
            Id = 12,
            ExternalOwnerId = owner.Id,
            ExternalOwner = owner,
        };

        owner.Dependents.Add(first);
        owner.Dependents.Add(second);
        context.Attach(owner);

        var dependent = new ExternalDependent
        {
            Id = 21,
            ExternalOwnerId = owner.Id,
            ExternalOwner = owner,
        };

        var entry = context.Entry(dependent);
        var update = NestedSetInsertionContract.Instance.Entry(entry);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var externalKey = entry
            .Metadata
            .GetForeignKeys()
            .Single();

        var ownerBefore = updates.FindPrincipal(update, externalKey);
        var ownerStateBefore = ownerBefore?.EntityState;
        var navigation = externalKey.PrincipalToDependent!;
        var collection = owner.Dependents;
        NestedSetCollectionBaseline? journal = null;

        // Act
        NestedSetCollectionBaseline.CaptureBeforeTracking(entry, ref journal);
        entry.State = EntityState.Added;
        owner.Dependents.Reverse();
        entry.State = EntityState.Detached;
        journal?.Restore();

        // Assert
        Assert.NotNull(journal);
        Assert.NotNull(ownerBefore);
        Assert.Equal(EntityState.Unchanged, ownerStateBefore);
        Assert.Same(
            owner,
            ownerBefore.ToEntityEntry()
                .Entity);

        Assert.Same(collection, owner.Dependents);
        Assert.Collection(owner.Dependents, value => Assert.Same(first, value), value => Assert.Same(second, value));

        var members =
            Assert.IsType<ISet<object>>(ownerBefore.GetRelationshipSnapshotValue(navigation), exactMatch: false);

        Assert.Equal(2, members.Count);
        Assert.Contains(first, members);
        Assert.Contains(second, members);
        Assert.DoesNotContain(dependent, members);
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Equal(EntityState.Unchanged, ownerBefore.EntityState);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A late-selected owner excludes active inputs from its first captured rollback baseline.</summary>
    [Fact]
    public async Task CaptureRelationships_LateSelectedOwnerBaseline_ExcludesIntroducedInputAndRetainsExistingMembers()
    {
        // Arrange
        await using var context = new BaselineContext();
        var initialOwner = new ExternalOwner { Id = 7 };
        var initialMember = new ExternalDependent
        {
            Id = 10,
            ExternalOwnerId = 7,
            ExternalOwner = initialOwner,
        };

        initialOwner.Dependents.Add(initialMember);
        var selectedOwner = new ExternalOwner { Id = 8 };
        var first = new ExternalDependent
        {
            Id = 11,
            ExternalOwnerId = 8,
            ExternalOwner = selectedOwner,
        };

        var second = new ExternalDependent
        {
            Id = 12,
            ExternalOwnerId = 8,
            ExternalOwner = selectedOwner,
        };

        selectedOwner.Dependents.Add(first);
        selectedOwner.Dependents.Add(second);
        context.AttachRange(initialOwner, selectedOwner);

        var input = new ExternalDependent
        {
            Id = 21,
            ExternalOwnerId = initialOwner.Id,
        };

        var entry = context.Entry(input);
        var update = NestedSetInsertionContract.Instance.Entry(entry);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var key = entry
            .Metadata
            .GetForeignKeys()
            .Single();

        var navigation = key.PrincipalToDependent!;
        var initialUpdate = NestedSetInsertionContract.Instance.Entry(context.Entry(initialOwner));
        var selectedUpdate = NestedSetInsertionContract.Instance.Entry(context.Entry(selectedOwner));
        var originalInitialCollection = initialOwner.Dependents;
        var originalSelectedCollection = selectedOwner.Dependents;
        var writes = new NestedSetCallbackWrites();
        var introducedEntries = new HashSet<IUpdateEntry>([update], ReferenceEqualityComparer.Instance);
        var introducedEntities = new HashSet<object>([input], ReferenceEqualityComparer.Instance);

        // Act
        writes.CaptureRelationships(entry);
        entry.State = EntityState.Added;
        var initialPrincipal = updates.FindPrincipal(update, key);
        var identity = NestedSetInsertionTracking.Capture(entry);

        // WHY: A callback can change an Added input's ordinary FK after its original owner was captured.
        // Public EF assignment performs real inverse fixup before the selected owner's first journal capture.
        entry.Property(value => value.ExternalOwnerId)
            .CurrentValue = selectedOwner.Id;

        identity.Refresh();
        context.ChangeTracker.AcceptAllChanges();
        var selectedPrincipal = updates.FindPrincipal(update, key);
        var selectedContainedInput = selectedOwner.Dependents.Contains(input);
        var selectedSnapshotContainedInput =
            (selectedUpdate.GetRelationshipSnapshotValue(navigation) as ISet<object>)?.Contains(input) == true;

        writes.RecordSavedRelationships([identity], introducedEntries, introducedEntities);
        identity.PrepareDetach();
        identity.Detach();
        writes.Restore();

        // Assert
        Assert.Same(initialUpdate, initialPrincipal);
        Assert.Same(selectedUpdate, selectedPrincipal);
        Assert.True(selectedContainedInput);
        Assert.True(selectedSnapshotContainedInput);
        Assert.Same(originalInitialCollection, initialOwner.Dependents);
        Assert.Same(initialMember, Assert.Single(initialOwner.Dependents));
        Assert.Same(originalSelectedCollection, selectedOwner.Dependents);
        Assert.Collection(
            selectedOwner.Dependents,
            value => Assert.Same(first, value),
            value => Assert.Same(second, value));

        var restoredSelected =
            Assert.IsType<ISet<object>>(selectedUpdate.GetRelationshipSnapshotValue(navigation), exactMatch: false);

        Assert.Equal(2, restoredSelected.Count);
        Assert.Contains(first, restoredSelected);
        Assert.Contains(second, restoredSelected);
        Assert.DoesNotContain(input, restoredSelected);

        var restoredInitial =
            Assert.IsType<ISet<object>>(initialUpdate.GetRelationshipSnapshotValue(navigation), exactMatch: false);

        Assert.Same(initialMember, Assert.Single(restoredInitial));
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Equal(EntityState.Unchanged, initialUpdate.EntityState);
        Assert.Equal(EntityState.Unchanged, selectedUpdate.EntityState);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
