using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    public async Task BeforeTrackingIgnoresOwnership(
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
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>An introduced ordinary principal does not create a retained external collection receipt.</summary>
    [Fact]
    public async Task BeforeTrackingIgnoresAddedExternalPrincipal()
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
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>An existing external owner retains CLR order and native baseline after input cleanup.</summary>
    [Fact]
    public async Task BeforeTrackingPreservesExistingExternalOwnerBaseline()
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
        Assert.Same(owner,
            ownerBefore.ToEntityEntry().Entity);
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
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>A late-selected owner excludes active inputs from its first captured rollback baseline.</summary>
    [Fact]
    public async Task LateSelectedOwnerBaselineExcludesIntroducedInputAndRetainsExistingMembers()
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
        entry.Property(value => value.ExternalOwnerId).CurrentValue = selectedOwner.Id;
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
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Known detached inputs preserve notification collections without native collection snapshots.</summary>
    [Fact]
    public async Task InputOwnerRollbackPreservesNotificationCollectionWithoutRetrackingKnownInput()
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
            input => ReferenceEquals(input, dependent) ? entry.Metadata : null,
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
        Assert.True(ownerEntry.Property(value => value.Payload).IsModified);
        Assert.False(ownerEntry.Property(value => value.Id).IsModified);
        Assert.Equal("Pending application payload", owner.Payload);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
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
    public async Task NotificationRollbackPreservesCollectionAndResumesSubscriptions(
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
        dependent.OwnerId = initiallyNull ? owner.Id : null;

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
            input => ReferenceEquals(input, dependent) ? entry.Metadata : null,
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
        var restoredPayloadModified = ownerEntry.Property(value => value.Payload).IsModified;

        var restoredPayload = owner.Payload;
        var finalOriginalNativeHandlers = original.NativeHandlerCount;
        var finalReplacementNativeHandlers = replacement.NativeHandlerCount;
        var applicationPropertiesBeforeProbe = applicationProperties;
        ownerEntry.Property(value => value.Payload).IsModified = false;
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
        Assert.True(ownerEntry.Property(value => value.Payload).IsModified);
        Assert.False(ownerEntry.Property(value => value.Id).IsModified);
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.DoesNotContain(updates.Entries, tracked => ReferenceEquals(tracked, update));
        Assert.Empty(updates.GetDependents(ownerUpdate, key));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Uses real ownership and ordinary relationships without opening a database connection.</summary>
    private sealed class BaselineContext : DbContext
    {
        /// <summary>Keeps payload entry creation detached until the test explicitly starts tracking.</summary>
        internal BaselineContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<ExternalOwner>()
                .Property(value => value.Id)
                .ValueGeneratedNever();

            var dependent = modelBuilder.Entity<ExternalDependent>();
            dependent
                .Property(value => value.Id)
                .ValueGeneratedNever();
            dependent
                .HasOne(value => value.ExternalOwner)
                .WithMany(value => value.Dependents)
                .HasForeignKey(value => value.ExternalOwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            var root = modelBuilder.Entity<OwnedRoot>();
            root
                .Property(value => value.Id)
                .ValueGeneratedNever();
            root.OwnsMany(
                value => value.Payloads,
                payload =>
                {
                    payload
                        .WithOwner(value => value.Root)
                        .HasForeignKey(value => value.RootId);

                    payload.HasKey(value => new
                    {
                        value.RootId,
                        value.Id,
                    });

                    payload
                        .Property(value => value.Id)
                        .ValueGeneratedNever();
                });
        }
    }

    /// <summary>Uses a notification owner with an ordinary, independently detachable child mapping.</summary>
    private class NotificationBaselineContext : DbContext
    {
        /// <summary>Retains caller-controlled detection during collection cleanup.</summary>
        internal NotificationBaselineContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <summary>Gets the notification strategy whose subscription lifetime is exercised.</summary>
        protected virtual ChangeTrackingStrategy TrackingStrategy =>
            ChangeTrackingStrategy.ChangingAndChangedNotifications;

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var owner = modelBuilder.Entity<NotificationOwner>();
            owner.HasChangeTrackingStrategy(TrackingStrategy);
            owner
                .Property(value => value.Id)
                .ValueGeneratedNever();

            var dependent = modelBuilder.Entity<NotificationDependent>();
            dependent
                .Property(value => value.Id)
                .ValueGeneratedNever();
            dependent
                .HasOne(value => value.Owner)
                .WithMany(value => value.Dependents)
                .HasForeignKey(value => value.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>Retains ordinary originals while using only changed notifications for fixup.</summary>
    private sealed class ChangedNotificationBaselineContext : NotificationBaselineContext
    {
        /// <inheritdoc />
        protected override ChangeTrackingStrategy TrackingStrategy => ChangeTrackingStrategy.ChangedNotifications;
    }

    /// <summary>Retains scalar originals while subscribing to both notification stages.</summary>
    private sealed class OriginalValueNotificationBaselineContext : NotificationBaselineContext
    {
        /// <inheritdoc />
        protected override ChangeTrackingStrategy TrackingStrategy =>
            ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues;
    }

    /// <summary>Owns payload rows introduced with one import root.</summary>
    private sealed class OwnedRoot
    {
        /// <summary>Gets or sets the fixed root key used before any provider-generated values exist.</summary>
        public int Id { get; set; }

        /// <summary>Gets the import-owned payload rows.</summary>
        public List<OwnedPayload> Payloads { get; } = [];
    }

    /// <summary>Represents an unrelated external collection owner that may already be persisted.</summary>
    private sealed class ExternalOwner
    {
        /// <summary>Gets or sets the external owner's fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets the ordinary inverse collection whose saved baseline requires protection.</summary>
        public List<ExternalDependent> Dependents { get; } = [];
    }

    /// <summary>Belongs only to its import root through EF ownership.</summary>
    private sealed class OwnedPayload
    {
        /// <summary>Gets or sets the local key within its root.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the ownership FK available while this entry is still Detached.</summary>
        public int RootId { get; set; }

        /// <summary>Gets or sets the ownership navigation.</summary>
        public required OwnedRoot Root { get; set; }
    }

    /// <summary>References an independently tracked principal through an ordinary, non-ownership FK.</summary>
    private sealed class ExternalDependent
    {
        /// <summary>Gets or sets the fixed dependent identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the ordinary external-owner FK before this input is tracked.</summary>
        public int ExternalOwnerId { get; set; }

        /// <summary>Gets or sets the ordinary reference, which may be null before detached-input fixup.</summary>
        public ExternalOwner? ExternalOwner { get; set; }
    }

    /// <summary>Exposes notification collections without EF collection relationship snapshots.</summary>
    private sealed class NotificationOwner : INotifyPropertyChanging, INotifyPropertyChanged
    {
        private int _id;
        private string _payload = string.Empty;
        private NotificationDependents? _dependents = new();

        /// <summary>Gets or sets the fixed owner key.</summary>
        public int Id
        {
            get => _id;
            set => Set(ref _id, value, nameof(Id));
        }

        /// <summary>Gets or sets ordinary pending application data whose modified flag must survive cleanup.</summary>
        public string Payload
        {
            get => _payload;
            set => Set(ref _payload, value, nameof(Payload));
        }

        /// <summary>Gets the collection whose exact reference and known detached member predate the callback.</summary>
        public NotificationDependents? Dependents
        {
            get => _dependents;
            set => Set(ref _dependents, value, nameof(Dependents));
        }

        /// <inheritdoc />
        public event PropertyChangingEventHandler? PropertyChanging;

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raises EF's actual notification pair around one scalar change.</summary>
        private void Set<TValue>(
            ref TValue field,
            TValue value,
            string propertyName
        )
        {
            if (EqualityComparer<TValue>.Default.Equals(field, value))
            {
                return;
            }

            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>Models a known import reference detached from its prior ordinary owner before capture.</summary>
    private sealed class NotificationDependent
    {
        /// <summary>Gets or sets the stable dependent key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional current FK independently of the stale prepared collection.</summary>
        public int? OwnerId { get; set; }

        /// <summary>Gets or sets the current ordinary owner reference.</summary>
        public NotificationOwner? Owner { get; set; }
    }

    /// <summary>Observes only this test collection's native EF subscriptions without private EF inspection.</summary>
    private sealed class NotificationDependents : ObservableCollection<NotificationDependent>
    {
        private NotifyCollectionChangedEventHandler? _changed;

        /// <summary>Allows EF's real collection accessor to instantiate a missing optional collection.</summary>
        public NotificationDependents() { }

        /// <summary>Gets the native EF handlers currently attached to this collection.</summary>
        internal int NativeHandlerCount =>
            _changed
                ?.GetInvocationList()
                .Count(handler => handler.Target is IUpdateEntry)
            ?? 0;

        /// <inheritdoc />
        public override event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        /// <inheritdoc />
        protected override void OnCollectionChanged(
            NotifyCollectionChangedEventArgs e
        )
        {
            base.OnCollectionChanged(e);
            _changed?.Invoke(this, e);
        }
    }
}
