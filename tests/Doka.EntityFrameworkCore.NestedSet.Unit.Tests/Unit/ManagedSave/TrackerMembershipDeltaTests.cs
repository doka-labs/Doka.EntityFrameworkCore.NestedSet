using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Restores only recorded native additions without replaying an unrelated ordinary owner graph.</summary>
public sealed class TrackerMembershipDeltaTests
{
    /// <summary>
    ///     Repeated rollback leaves original members and CLR state intact even after earlier native cleanup.
    /// </summary>
    /// <param name="removedBeforeUndo">Whether earlier cleanup already removed the installed native member.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreIsIdempotentAndPreservesUnownedMembership(
        bool removedBeforeUndo
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
        var owner = new HierarchyChangeDetectionTests.ProbeOwner { Id = 7 };
        var original = HierarchyChangeDetectionTests.CreateNode();
        owner.ScopeNodes.Add(original);
        context.Attach(owner);
        var ownerEntry = context.Entry(owner);
        owner.Payload = "Pending owner payload";
        ownerEntry.Property(value => value.Payload)
            .IsModified = true;

        var ownerUpdate = HierarchyChangeDetectionTests.UpdateEntry(context, owner);
        var navigation = ownerUpdate.EntityType.FindNavigation(nameof(owner.ScopeNodes))!;
        var nativeBefore = ownerUpdate.GetRelationshipSnapshotValue(navigation);
        var collection = owner.ScopeNodes;
        var snapshot = NestedSetTrackerSnapshot.Capture(context, []);
        var member = HierarchyChangeDetectionTests.CreateNode(2);
        member.TreeId = Guid.NewGuid();
        owner.ScopeNodes.Add(member);
        context.Attach(member);
        var contract = NestedSetInsertionContract.Instance;
        var containedBefore = (nativeBefore as ISet<object>)?.Contains(member) == true;

        // Act
        snapshot.RecordPersistedAddition(ownerUpdate, navigation, member);
        contract.AddCollectionSnapshot(ownerUpdate, navigation, member);

        if (removedBeforeUndo)
        {
            // WHY: EF's native fixup or earlier cleanup may already have removed this exact addition.
            // An inverse delta must tolerate that state rather than reconstructing a complete owner snapshot.
            contract.RemoveCollectionSnapshot(ownerUpdate, navigation, member);
        }

        snapshot.Restore();
        var afterFirst = ownerUpdate.GetRelationshipSnapshotValue(navigation);
        snapshot.Restore();

        // Assert
        Assert.False(containedBefore);
        Assert.Same(nativeBefore, afterFirst);
        Assert.Same(afterFirst, ownerUpdate.GetRelationshipSnapshotValue(navigation));
        Assert.Same(original, Assert.Single(Assert.IsAssignableFrom<ISet<object>>(afterFirst)));
        Assert.Same(collection, owner.ScopeNodes);
        Assert.Collection(
            owner.ScopeNodes,
            node => Assert.Same(original, node),
            node => Assert.Same(member, node));

        Assert.Equal("Pending owner payload", owner.Payload);
        Assert.Equal(EntityState.Modified, ownerEntry.State);
        Assert.True(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            ownerEntry.Property(value => value.Id)
                .IsModified);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(original)
                .State);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(member)
                .State);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(CascadeTiming.OnSaveChanges, context.ChangeTracker.CascadeDeleteTiming);
        Assert.Equal(CascadeTiming.OnSaveChanges, context.ChangeTracker.DeleteOrphansTiming);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>An absent native collection slot remains absent when an already-cleared addition is undone.</summary>
    [Fact]
    public async Task RestoreDoesNotCreateMissingNativeMembershipSlot()
    {
        // Arrange
        await using var context = new NullSlotContext();
        var owner = new NullSlotOwner { Id = 7 };
        context.Attach(owner);
        var ownerUpdate = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Single();

        var navigation = ownerUpdate.EntityType.FindNavigation(nameof(owner.Members))!;
        var snapshot = NestedSetTrackerSnapshot.Capture(context, []);
        var member = new NullSlotMember
        {
            Id = 1,
            OwnerId = owner.Id,
        };

        var collection = new List<NullSlotMember> { member };
        owner.Members = collection;
        context.Attach(member);

        // WHY: The owner was tracked with a null collection. Preparing CLR membership before child fixup
        // leaves its actual native slot null; an owner tracked with an empty list already has an empty set.
        var nativeBefore = ownerUpdate.GetRelationshipSnapshotValue(navigation);

        // Act
        snapshot.RecordPersistedAddition(ownerUpdate, navigation, member);
        snapshot.Restore();
        var afterFirst = ownerUpdate.GetRelationshipSnapshotValue(navigation);
        snapshot.Restore();

        // Assert
        Assert.Null(nativeBefore);
        Assert.Null(afterFirst);
        Assert.Null(ownerUpdate.GetRelationshipSnapshotValue(navigation));
        Assert.Same(collection, owner.Members);
        Assert.Same(member, Assert.Single(owner.Members!));
        Assert.Same(owner, member.Owner);
        Assert.Equal(EntityState.Unchanged, ownerUpdate.EntityState);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(member)
                .State);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Tracks an initially null collection without fabricating EF's native snapshot storage.</summary>
    private sealed class NullSlotContext : DbContext
    {
        /// <summary>Keeps all graph changes explicit throughout this model-only rollback control.</summary>
        internal NullSlotContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
            => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<NullSlotOwner>()
                .Property(owner => owner.Id)
                .ValueGeneratedNever();

            modelBuilder
                .Entity<NullSlotMember>()
                .Property(member => member.Id)
                .ValueGeneratedNever();

            modelBuilder
                .Entity<NullSlotMember>()
                .HasOne(member => member.Owner)
                .WithMany(owner => owner.Members)
                .HasForeignKey(member => member.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>Provides nullable caller storage so EF captures a genuine absent collection slot.</summary>
    private sealed class NullSlotOwner
    {
        /// <summary>Gets or sets the fixed ordinary principal key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the collection independently of EF's native relationship snapshot.</summary>
        public List<NullSlotMember>? Members { get; set; }
    }

    /// <summary>Uses actual ordinary FK fixup without hierarchy configuration or database I/O.</summary>
    private sealed class NullSlotMember
    {
        /// <summary>Gets or sets the fixed ordinary dependent key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the tracked principal key.</summary>
        public int OwnerId { get; set; }

        /// <summary>Gets or sets the reference populated by native EF fixup.</summary>
        public NullSlotOwner? Owner { get; set; }
    }
}
