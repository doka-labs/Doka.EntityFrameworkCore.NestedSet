using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Separates native relationship recovery from unrelated pending CLR container changes.</summary>
public sealed class SupplementalRelationshipRollbackTests
{
    /// <summary>A null preimage must not discard unrelated raw members introduced into a new collection.</summary>
    /// <param name="pendingMember">Whether an unrelated raw member requires retaining the new container.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullCollectionPreservesOnlyUnrelatedPendingMembers(
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
    public async Task RoundTripPreservesNativePresenceAndIndependentRawOrder(
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

    /// <summary>Required-FK recovery preserves actual bucket preimages independently of scalar/null flags.</summary>
    /// <param name="initial">The ordinary EF operations that prepare the captured relationship.</param>
    /// <param name="callback">The ordinary EF operations applied after capture.</param>
    [Theory]
    [InlineData("attached", "reference-null")]
    [InlineData("attached", "scalar-roundtrip")]
    [InlineData("attached", "unmarked-roundtrip")]
    [InlineData("reference-null", "attached")]
    [InlineData("reference-null", "raw-reset")]
    [InlineData("scalar-null", "scalar-attached")]
    [InlineData("scalar-roundtrip", "attached")]
    [InlineData("unmarked-roundtrip", "attached")]
    public async Task RequiredConceptualNullRestoresOriginalNativeRelationship(
        string initial,
        string callback
    )
    {
        // Arrange
        await using var context = new RequiredContext();
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
        var owner = new Owner
        {
            Id = 2,
            Nodes = [],
        };

        var node = new Node
        {
            Id = 10,
            OwnerId = owner.Id,
            Owner = owner,
        };

        owner.Nodes.Add(node);
        context.Attach(owner);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var update = updates.Entries.Single(entry => ReferenceEquals(
            entry.ToEntityEntry()
                .Entity,
            node));

        var principal = updates.Entries.Single(entry => ReferenceEquals(
            entry.ToEntityEntry()
                .Entity,
            owner));

        var reference = update.EntityType.FindNavigation(nameof(Node.Owner))!;
        var property = update.EntityType.FindProperty(nameof(Node.OwnerId))!;

        // WHY: EF's public read initializes its lazy map before the tested initial changes. Otherwise the
        // first later lookup would rebuild membership from current values and conceal these distinct states.
        var initialBucket = updates.GetDependents(principal, reference.ForeignKey);
        var initiallyInstalled = initialBucket.ToArray();
        PrepareRequiredRelationship(context, node, owner, initial);
        var originalNull = update.IsConceptualNull(property);
        var originalModified = update.IsModified(property);
        var originalState = update.EntityState;
        var originalReference = node.Owner;
        var originalSnapshot = update.GetRelationshipSnapshotValue(reference);
        var originalMembers = owner.Nodes!.ToArray();
        var originalNativeMembers = Assert
            .IsAssignableFrom<ISet<object>>(
                principal.GetRelationshipSnapshotValue(reference.Inverse!))
            .ToArray();

        var originalDependents = updates
            .GetDependents(principal, reference.ForeignKey)
            .ToArray();

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        PrepareRequiredRelationship(context, node, owner, callback);
        var callbackNull = update.IsConceptualNull(property);

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.IsType<HashSet<IUpdateEntry>>(initialBucket);
        Assert.Same(update, Assert.Single(initiallyInstalled));
        Assert.Equal(initial is "reference-null" or "scalar-null", originalNull);
        Assert.Equal(initial is "attached" or "scalar-null", originalDependents.Length == 1);
        Assert.Equal(callback == "reference-null", callbackNull);
        Assert.Equal(originalNull, update.IsConceptualNull(property));
        Assert.Equal(originalModified, update.IsModified(property));
        Assert.Equal(owner.Id, node.OwnerId);
        Assert.Same(originalReference, node.Owner);
        Assert.Equal(originalMembers, owner.Nodes!);
        Assert.Same(originalSnapshot, update.GetRelationshipSnapshotValue(reference));
        Assert.Equal(originalDependents, updates.GetDependents(principal, reference.ForeignKey));
        var nativeMembers = Assert.IsAssignableFrom<ISet<object>>(
            principal.GetRelationshipSnapshotValue(reference.Inverse!));

        Assert.True(nativeMembers.SetEquals(originalNativeMembers));
        Assert.Equal(originalState, update.EntityState);
        Assert.Empty(
            NestedSetInsertionContract.Instance.GetOrCreateDependents(
                NestedSetInsertionContract.Instance.Manager(update),
                [3],
                reference.ForeignKey));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Preserves a native bucket first initialized after an unrelated raw FK edit.</summary>
    /// <param name="initializeBeforeRawEdit">Whether native membership was indexed before the pending raw edit.</param>
    /// <param name="conceptualNull">Whether a required-FK null flag additionally hides that raw key.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DivergentRawForeignKeyPreservesActualIndexedBucket(
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

    /// <summary>
    ///     Uses public EF operations to create distinct required-FK states without fabricating native maps.
    /// </summary>
    private static void PrepareRequiredRelationship(
        SupplementalContext context,
        Node node,
        Owner owner,
        string state
    )
    {
        var entry = context.Entry(node);

        switch (state)
        {
            case "attached":
                entry.Reference(value => value.Owner)
                    .CurrentValue = owner;

                break;
            case "reference-null":
                entry.Reference(value => value.Owner)
                    .CurrentValue = null;

                break;
            case "scalar-attached":
                entry.Property(value => value.OwnerId)
                    .CurrentValue = owner.Id;

                break;
            case "scalar-null":
                entry.Property(value => value.OwnerId)
                    .CurrentValue = null;

                break;
            case "raw-reset":
                entry.Property(value => value.OwnerId)
                    .CurrentValue = 3;

                node.OwnerId = owner.Id;
                break;
            case "scalar-roundtrip":
            case "unmarked-roundtrip":
                entry.Reference(value => value.Owner)
                    .CurrentValue = null;

                entry.Property(value => value.OwnerId)
                    .CurrentValue = owner.Id;

                if (state == "unmarked-roundtrip")
                {
                    entry.Property(value => value.OwnerId)
                        .IsModified = false;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    /// <summary>Provides real EF snapshots for an ordinary nullable inverse collection.</summary>
    private class SupplementalContext : DbContext
    {
        /// <summary>Keeps native fixup distinct from manual raw graph edits.</summary>
        internal SupplementalContext()
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
                .Entity<Owner>()
                .Property(owner => owner.Id)
                .ValueGeneratedNever();

            modelBuilder
                .Entity<Node>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            modelBuilder
                .Entity<Node>()
                .HasOne(node => node.Owner)
                .WithMany(owner => owner.Nodes)
                .HasForeignKey(node => node.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>Uses EF's required relationship/orphan semantics with an independently nullable CLR member.</summary>
    private sealed class RequiredContext : SupplementalContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<Node>()
                .Property(node => node.OwnerId)
                .IsRequired();

            modelBuilder
                .Entity<Node>()
                .HasOne(node => node.Owner)
                .WithMany(owner => owner.Nodes)
                .HasForeignKey(node => node.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Separates nullable CLR collection storage from its native membership set.</summary>
    private sealed class Owner
    {
        /// <summary>Gets or sets the fixed ordinary principal key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets nullable caller-owned collection storage.</summary>
        public List<Node>? Nodes { get; set; }
    }

    /// <summary>Supplies real ordinary FK fixup without hierarchy or database work.</summary>
    private sealed class Node
    {
        /// <summary>Gets or sets the fixed dependent key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the nullable ordinary principal key.</summary>
        public int? OwnerId { get; set; }

        /// <summary>Gets or sets the ordinary principal reference.</summary>
        public Owner? Owner { get; set; }
    }
}
