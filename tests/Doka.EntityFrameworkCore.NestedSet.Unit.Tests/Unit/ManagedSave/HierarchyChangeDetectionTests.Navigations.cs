using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>A changed parent reference is detected before EF copies its key into the scalar foreign key.</summary>
    [Fact]
    public async Task HasHierarchyChanges_ParentReferenceChange_DoesNotRequireForeignKeyFixup()
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
    public async Task HasHierarchyChanges_ClearingParentReference_RemainsGuardedAfterPrincipalDetach()
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
    public async Task HasHierarchyChanges_ParentOriginalOverride_DoesNotHideRelationshipChange()
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
    public async Task HasHierarchyChanges_ChildrenMembershipChanges_AreObservedBeforeFixup(
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
    public async Task HasHierarchyChanges_DetachedChildCollectionChange_DoesNotAttachChild()
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
    public async Task HasHierarchyChanges_PrincipalReference_UsesRelevantKeyWithoutAttachingReplacement(
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
    public async Task HasHierarchyChanges_ReplacingChildrenCollection_PreservesSetSemantics()
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
    public async Task HasHierarchyChanges_NullAndEmptyChildrenCollections_AreEquivalent(
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
}
