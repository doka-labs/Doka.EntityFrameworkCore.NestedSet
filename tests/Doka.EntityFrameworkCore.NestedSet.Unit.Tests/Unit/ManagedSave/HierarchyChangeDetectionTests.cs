using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects read-only hierarchy probes when the application controls EF change detection.</summary>
public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Ordinary payload edits remain invisible to EF until the application requests detection.</summary>
    [Fact]
    public async Task PayloadChangesDoNotMutateHierarchyOrPrincipalTracking()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var owner = new ProbeOwner
        {
            Id = 9,
            Payload = "Original owner",
        };

        var node = CreateNode();
        node.OwnerId = owner.Id;
        node.Owner = owner;
        context.Attach(node);
        var nodeEntry = context.Entry(node);
        var ownerEntry = context.Entry(owner);
        node.Payload = "Pending node payload";
        owner.Payload = "Pending principal payload";

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(EntityState.Unchanged, nodeEntry.State);
        Assert.Equal(EntityState.Unchanged, ownerEntry.State);
        Assert.False(
            nodeEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A normal principal's collection can change the hierarchy scope before FK fixup runs.</summary>
    [Fact]
    public async Task ScopePrincipalCollectionChangeDoesNotDetectItsPayload()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var owner = new ProbeOwner
        {
            Id = 9,
            Payload = "Original owner",
        };

        var node = CreateNode();
        context.AttachRange(owner, node);
        var ownerEntry = context.Entry(owner);
        var nodeEntry = context.Entry(node);
        owner.Payload = "Pending principal payload";
        owner.ScopeNodes.Add(node);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(7, node.Scope);
        Assert.Null(node.ScopeOwner);
        Assert.Equal(EntityState.Unchanged, ownerEntry.State);
        Assert.Equal(EntityState.Unchanged, nodeEntry.State);
        Assert.False(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            nodeEntry.Property(value => value.Scope)
                .IsModified);
    }

    /// <summary>A base-declared scope FK remains relevant when only its concrete TPC subtype is a hierarchy.</summary>
    [Fact]
    public async Task InheritedScopePrincipalCollectionChangeIsObserved()
    {
        // Arrange
        await using var context = new InheritedScopeContext();
        var owner = new InheritedScopeOwner
        {
            Id = 9,
            Payload = "Original owner",
        };

        var node = new InheritedScopeNode
        {
            Id = 1,
            Scope = 7,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
        };

        context.AttachRange(owner, node);
        var ownerEntry = context.Entry(owner);
        var nodeEntry = context.Entry(node);
        owner.Payload = "Pending principal payload";
        owner.Nodes.Add(node);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(7, node.Scope);
        Assert.Null(node.ScopeOwner);
        Assert.Equal(EntityState.Unchanged, ownerEntry.State);
        Assert.Equal(EntityState.Unchanged, nodeEntry.State);
        Assert.False(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            nodeEntry.Property(value => value.Scope)
                .IsModified);

        var foreignKey = nodeEntry
            .Metadata
            .GetForeignKeys()
            .Single(key => key.Properties.Count == 1 && key.Properties[0].Name == nameof(InheritedScopeNode.Scope));

        Assert.Equal(typeof(InheritedScopeBase), foreignKey.DeclaringEntityType.ClrType);
    }

    /// <summary>Ordinary siblings in a mixed inherited collection do not create hierarchy work.</summary>
    /// <param name="remove">Whether an existing ordinary member is removed instead of being added.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryInheritedCollectionChangePreservesUntouchedHierarchy(
        bool remove
    )
    {
        // Arrange
        await using var context = new InheritedScopeContext();
        var owner = new InheritedScopeOwner
        {
            Id = 7,
            Payload = "Original owner",
        };

        var hierarchy = new InheritedScopeNode
        {
            Id = 1,
            Scope = owner.Id,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
        };

        var ordinary = new OrdinaryScopeNode
        {
            Id = 2,
            Scope = remove
                ? owner.Id
                : 9,
        };

        context.AttachRange(owner, hierarchy, ordinary);
        var ownerEntry = context.Entry(owner);
        var hierarchyEntry = context.Entry(hierarchy);
        var ordinaryEntry = context.Entry(ordinary);
        owner.Payload = "Pending principal payload";

        if (remove)
        {
            owner.Nodes.Remove(ordinary);
        }
        else
        {
            owner.Nodes.Add(ordinary);
        }

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Same(owner, hierarchy.ScopeOwner);
        Assert.Equal(owner.Id, hierarchy.Scope);
        Assert.Contains(hierarchy, owner.Nodes);
        Assert.Equal(EntityState.Unchanged, ownerEntry.State);
        Assert.Equal(EntityState.Unchanged, hierarchyEntry.State);
        Assert.Equal(EntityState.Unchanged, ordinaryEntry.State);
        Assert.False(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            hierarchyEntry.Property(value => value.Scope)
                .IsModified);

        Assert.False(
            ordinaryEntry.Property(value => value.Scope)
                .IsModified);
    }

    /// <summary>An ordinary inherited dependent's reference edit cannot activate a sibling hierarchy mapping.</summary>
    [Fact]
    public async Task OrdinaryInheritedReferenceChangePreservesUntouchedHierarchy()
    {
        // Arrange
        await using var context = new InheritedScopeContext();
        var owner = new InheritedScopeOwner
        {
            Id = 7,
            Payload = "Original owner",
        };

        var replacement = new InheritedScopeOwner { Id = 9 };
        var hierarchy = new InheritedScopeNode
        {
            Id = 1,
            Scope = owner.Id,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
        };

        var ordinary = new OrdinaryScopeNode
        {
            Id = 2,
            Scope = owner.Id,
        };

        context.AttachRange(owner, replacement, hierarchy, ordinary);
        var ownerEntry = context.Entry(owner);
        var hierarchyEntry = context.Entry(hierarchy);
        var ordinaryEntry = context.Entry(ordinary);
        owner.Payload = "Pending principal payload";
        ordinary.ScopeOwner = replacement;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Same(owner, hierarchy.ScopeOwner);
        Assert.Equal(owner.Id, hierarchy.Scope);
        Assert.Equal(owner.Id, ordinary.Scope);
        Assert.Same(replacement, ordinary.ScopeOwner);
        Assert.Equal(EntityState.Unchanged, ownerEntry.State);
        Assert.Equal(EntityState.Unchanged, hierarchyEntry.State);
        Assert.Equal(EntityState.Unchanged, ordinaryEntry.State);
        Assert.False(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            hierarchyEntry.Property(value => value.Scope)
                .IsModified);

        Assert.False(
            ordinaryEntry.Property(value => value.Scope)
                .IsModified);
    }

    /// <summary>A duplicate hierarchy reference does not change membership in a mixed inherited collection.</summary>
    [Fact]
    public async Task MixedInheritedCollectionRetainsReferenceSetSemantics()
    {
        // Arrange
        await using var context = new InheritedScopeContext();
        var owner = new InheritedScopeOwner { Id = 7 };
        var hierarchy = new InheritedScopeNode
        {
            Id = 1,
            Scope = owner.Id,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
        };

        var ordinary = new OrdinaryScopeNode
        {
            Id = 2,
            Scope = owner.Id,
        };

        context.AttachRange(owner, hierarchy, ordinary);
        owner.Nodes.Add(hierarchy);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(2, owner.Nodes.Count(value => ReferenceEquals(value, hierarchy)));
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(hierarchy)
                .State);
    }

    /// <summary>Every structural role and configured order criterion is checked before flags are populated.</summary>
    /// <param name="propertyName">The configured role or ordering property whose CLR value changes.</param>
    [Theory]
    [InlineData(nameof(ProbeNode.Id))]
    [InlineData(nameof(ProbeNode.Scope))]
    [InlineData(nameof(ProbeNode.TreeId))]
    [InlineData(nameof(ProbeNode.ParentId))]
    [InlineData(nameof(ProbeNode.Left))]
    [InlineData(nameof(ProbeNode.Right))]
    [InlineData(nameof(ProbeNode.Depth))]
    [InlineData(nameof(ProbeNode.Position))]
    [InlineData(nameof(ProbeNode.Name))]
    public async Task UnmarkedRoleChangesAreObservedWithoutMarkingProperties(
        string propertyName
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        context.Attach(node);
        var entry = context.Entry(node);
        ChangeRole(node, propertyName);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(
            entry.Property(propertyName)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Explicit flags remain authoritative when current and original scalar values are equal.</summary>
    [Fact]
    public async Task ExplicitOrderingFlagIsObservedWithEqualValues()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        context.Attach(node);
        var entry = context.Entry(node);
        entry.Property(value => value.Name)
            .IsModified = true;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.True(
            entry.Property(value => value.Name)
                .IsModified);
    }

    /// <summary>Added and deleted hierarchy entries require coordination without any scalar comparison.</summary>
    /// <param name="state">The explicit pending insertion or deletion state.</param>
    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Deleted)]
    public async Task PendingHierarchyLifecycleIsObserved(
        EntityState state
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        var entry = context.Attach(node);
        entry.State = state;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(state, entry.State);
    }

    /// <summary>Configured scalar equality is honored rather than falling back to CLR string equality.</summary>
    [Fact]
    public async Task EquivalentOrderingValueUsesConfiguredComparer()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        context.Attach(node);
        var entry = context.Entry(node);
        node.Name = "ALPHA";

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(
            entry.Property(value => value.Name)
                .IsModified);
    }

    /// <summary>A changed parent reference is detected before EF copies its key into the scalar foreign key.</summary>
    [Fact]
    public async Task ParentReferenceChangeDoesNotRequireForeignKeyFixup()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var original = CreateNode();
        var replacement = CreateNode(3);
        var child = CreateNode(2);
        child.ParentId = original.Id;
        child.Parent = original;
        context.AttachRange(original, replacement, child);
        var entry = context.Entry(child);
        child.Parent = replacement;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(original.Id, child.ParentId);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(
            entry.Property(value => value.ParentId)
                .IsModified);
    }

    /// <summary>Clearing a dependent reference still changes its FK after the old principal is detached.</summary>
    [Fact]
    public async Task ClearingParentReferenceRemainsGuardedAfterPrincipalDetach()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var parent = CreateNode();
        var child = CreateNode(2);
        child.ParentId = parent.Id;
        child.Parent = parent;
        context.Attach(child);
        var entry = context.Entry(child);
        context.Entry(parent)
            .State = EntityState.Detached;

        child.Parent = null;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.Null(child.Parent);
        Assert.Equal(parent.Id, child.ParentId);
        Assert.Equal(
            parent.Id,
            entry.Property(value => value.ParentId)
                .OriginalValue);

        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(
            entry.Property(value => value.ParentId)
                .IsModified);

        Assert.Equal(
            EntityState.Detached,
            context.Entry(parent)
                .State);

        var navigation = entry.Metadata.FindNavigation(nameof(ProbeNode.Parent))!;

        Assert.Same(
            parent,
            UpdateEntry(context, child)
                .GetRelationshipSnapshotValue(navigation));

        Assert.True(changed);
    }

    /// <summary>Overriding a scalar original does not erase EF's separately captured parent relationship.</summary>
    [Fact]
    public async Task ParentOriginalOverrideDoesNotHideRelationshipChange()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var parent = CreateNode();
        var replacement = CreateNode(3);
        var child = CreateNode(2);
        child.ParentId = parent.Id;
        child.Parent = parent;
        context.AttachRange(parent, replacement, child);
        var entry = context.Entry(child);
        child.ParentId = replacement.Id;
        child.Payload = "Undetected payload";
        entry.Property(value => value.ParentId)
            .OriginalValue = replacement.Id;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Same(parent, child.Parent);
        Assert.Equal(
            replacement.Id,
            entry.Property(value => value.ParentId)
                .OriginalValue);

        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(
            entry.Property(value => value.ParentId)
                .IsModified);

        Assert.False(
            entry.Property(value => value.Payload)
                .IsModified);
    }

    /// <summary>Collection membership edits are visible without fixing the dependent's foreign key.</summary>
    /// <param name="remove">Whether the original member is removed instead of a new member being added.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildrenMembershipChangesAreObservedBeforeFixup(
        bool remove
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var parent = CreateNode();
        var child = CreateNode(2);

        if (remove)
        {
            child.ParentId = parent.Id;
            child.Parent = parent;
            parent.Children!.Add(child);
        }

        context.AttachRange(parent, child);
        var entry = context.Entry(child);

        if (remove)
        {
            parent.Children!.Remove(child);
        }
        else
        {
            parent.Children!.Add(child);
        }

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(
            remove
                ? parent.Id
                : null,
            child.ParentId);

        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(
            entry.Property(value => value.ParentId)
                .IsModified);
    }

    /// <summary>A detached child in a tracked parent's collection is observed without tracking the child.</summary>
    [Fact]
    public async Task DetachedChildCollectionChangeDoesNotAttachChild()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var parent = CreateNode();
        context.Attach(parent);
        var child = CreateNode(2);
        parent.Children!.Add(child);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(parent)
                .State);

        Assert.Equal(
            EntityState.Detached,
            context.Entry(child)
                .State);

        Assert.Single(context.ChangeTracker.Entries());
        Assert.Null(child.ParentId);
        Assert.Null(child.Parent);
    }

    /// <summary>Principal references are guarded by their relevant key without attaching replacements.</summary>
    /// <param name="loaded">Whether the original principal is already tracked and its navigation fixed up.</param>
    /// <param name="sameKey">Whether the replacement preserves the current hierarchy scope.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PrincipalReferenceUsesRelevantKeyWithoutAttachingReplacement(
        bool loaded,
        bool sameKey
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var owner = new ProbeOwner { Id = 7 };
        var node = CreateNode();

        if (loaded)
        {
            context.Attach(owner);
        }

        context.Attach(node);
        var replacement = new ProbeOwner
        {
            Id = sameKey
                ? owner.Id
                : owner.Id + 1,
        };

        node.ScopeOwner = replacement;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.Equal(!sameKey, changed);
        Assert.Same(replacement, node.ScopeOwner);
        Assert.Equal(owner.Id, node.Scope);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(node)
                .State);

        Assert.Equal(
            loaded
                ? EntityState.Unchanged
                : EntityState.Detached,
            context.Entry(owner)
                .State);

        Assert.Equal(
            EntityState.Detached,
            context.Entry(replacement)
                .State);

        Assert.Equal(
            loaded
                ? 2
                : 1,
            context
                .ChangeTracker
                .Entries()
                .Count());

        Assert.False(
            context
                .Entry(node)
                .Property(value => value.Scope)
                .IsModified);
    }

    /// <summary>A different collection object with the same members does not introduce hierarchy work.</summary>
    [Fact]
    public async Task ReplacingChildrenCollectionPreservesSetSemantics()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var parent = CreateNode();
        var child = CreateNode(2);
        child.ParentId = parent.Id;
        child.Parent = parent;
        parent.Children!.Add(child);
        context.Attach(parent);
        parent.Children = [child];

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(parent.Id, child.ParentId);
    }

    /// <summary>EF treats null and empty relationship collections as the same membership set.</summary>
    /// <param name="initiallyNull">Whether null is the captured collection rather than its replacement.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullAndEmptyChildrenCollectionsAreEquivalent(
        bool initiallyNull
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        node.Children = initiallyNull
            ? null
            : [];

        context.Attach(node);
        node.Children = initiallyNull
            ? []
            : null;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(node)
                .State);
    }

    /// <summary>Notification collection navigations have no relationship snapshot slot to inspect.</summary>
    [Fact]
    public async Task UnchangedNotificationCollectionDoesNotReadMissingSnapshotSlot()
    {
        // Arrange
        await using var context = new NotificationContext();
        var parent = CreateNode();
        var child = CreateNode(2);
        child.ParentId = parent.Id;
        child.Parent = parent;
        parent.Children!.Add(child);
        context.Attach(parent);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(child)
                .State);
    }

    /// <summary>Notification fixup flags identify collection changes without collection snapshot storage.</summary>
    [Fact]
    public async Task NotificationCollectionChangeUsesForeignKeyModification()
    {
        // Arrange
        await using var context = new NotificationContext();
        var parent = CreateNode();
        var child = CreateNode(2);
        context.AttachRange(parent, child);
        parent.Children!.Add(child);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(parent.Id, child.ParentId);
        Assert.True(
            context
                .Entry(child)
                .Property(value => value.ParentId)
                .IsModified);
    }

    /// <summary>Shadow values use their tracked storage rather than requiring a CLR property getter.</summary>
    /// <param name="change">Whether a shadow scalar changes after the original value has been captured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShadowValuesRespectTrackedModificationState(
        bool change
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var owner = new ProbeOwner { Id = 9 };
        context.Attach(owner);
        var property = context
            .Entry(owner)
            .Property<int>("ShadowOrder");

        if (change)
        {
            property.CurrentValue = 2;
        }

        var update = UpdateEntry(context, owner);

        // Act
        var changed = NestedSetTrackedProperty.HasChanged(update, property.Metadata);

        // Assert
        Assert.Equal(change, changed);
        Assert.Equal(change, property.IsModified);
    }

    /// <summary>Null comparison follows EF's object-comparer guard without calling custom equality on nulls.</summary>
    /// <param name="original">The value captured when tracking begins.</param>
    /// <param name="current">The unmarked CLR value observed by the scalar probe.</param>
    /// <param name="expected">Whether EF's null-safe equality considers the values different.</param>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, "Alpha", true)]
    [InlineData("Alpha", null, true)]
    [InlineData("Alpha", "Alpha", false)]
    [InlineData("Alpha", "Bravo", true)]
    public async Task NullableCustomComparerIsNotInvokedWithNullValues(
        string? original,
        string? current,
        bool expected
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var owner = new ProbeOwner
        {
            Id = 9,
            StrictText = original,
        };

        context.Attach(owner);
        var entry = context.Entry(owner);
        var property = entry.Property(value => value.StrictText);
        owner.StrictText = current;
        var update = UpdateEntry(context, owner);

        // Act
        var changed = NestedSetTrackedProperty.HasChanged(update, property.Metadata);

        // Assert
        Assert.Equal(expected, changed);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(property.IsModified);
    }

    /// <summary>Conceptual nulls remain structural work even when EF retains the non-null CLR sentinel.</summary>
    [Fact]
    public async Task ConceptualNullRoleIsObservedWithoutReplacingClrValue()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        context.Attach(node);

        // WHY: Retain EF's conceptual-null state for inspection instead of immediately processing the orphan.
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
        var property = context
            .Entry(node)
            .Property(nameof(ProbeNode.Scope));

        // WHY: EF records null for this required scalar in its conceptual-null flag instead of an Int32 field.
        property.CurrentValue = null;
        var update = UpdateEntry(context, node);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.True(update.IsConceptualNull(property.Metadata));
        Assert.Equal(7, node.Scope);
    }

    /// <summary>A conceptual null remains visible when flags and all scalar snapshots otherwise agree.</summary>
    [Fact]
    public async Task ConceptualNullWithoutModifiedFlagIsStillHierarchyWork()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        context.Attach(node);
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
        var property = context
            .Entry(node)
            .Property(value => value.Scope)
            .Metadata;

        var update = UpdateEntry(context, node);

        // WHY: The existing framework seam sets setModified=false, unlike PropertyEntry.CurrentValue. This
        // isolates conceptual null from equal CLR/original/relationship values and independent Modified flags.
        NestedSetInsertionContract.Instance.SetProperty(update, property, null);

        // Act
        var scalarChanged = NestedSetTrackedProperty.HasChanged(update, property);
        var hierarchyChanged = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(scalarChanged);
        Assert.True(hierarchyChanged);
        Assert.True(update.IsConceptualNull(property));
        Assert.False(update.IsModified(property));
        Assert.Equal(7, update.GetCurrentValue<int>(property));
        Assert.Equal(7, update.GetOriginalValue<int>(property));
        Assert.Equal(7, update.GetRelationshipSnapshotValue(property));
        Assert.Equal(EntityState.Unchanged, update.EntityState);
    }

    /// <summary>Finds the existing provider entry without invoking EF's automatic detection surface.</summary>
    internal static IUpdateEntry UpdateEntry(
        DbContext context,
        object entity
    ) => context
        .GetService<IUpdateAdapterFactory>()
        .Create()
        .Entries
        .Single(entry => ReferenceEquals(
            entry.ToEntityEntry()
                .Entity,
            entity));
}
