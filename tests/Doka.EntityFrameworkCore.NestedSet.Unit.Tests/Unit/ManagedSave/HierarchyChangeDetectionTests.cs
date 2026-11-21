using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects read-only hierarchy probes when the application controls EF change detection.</summary>
public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Ordinary payload edits remain invisible to EF until the application requests detection.</summary>
    [Fact]
    public async Task HasHierarchyChanges_PayloadChanges_DoNotMutateHierarchyOrPrincipalTracking()
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
    public async Task HasHierarchyChanges_ScopePrincipalCollectionChange_DoesNotDetectItsPayload()
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
    public async Task HasHierarchyChanges_UnmarkedRoleChanges_AreObservedWithoutMarkingProperties(
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
    public async Task HasHierarchyChanges_ExplicitOrderingFlag_IsObservedWithEqualValues()
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
    public async Task HasHierarchyChanges_PendingHierarchyLifecycle_IsObserved(
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
    public async Task HasHierarchyChanges_EquivalentOrderingValue_UsesConfiguredComparer()
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

    /// <summary>Notification collection navigations have no relationship snapshot slot to inspect.</summary>
    [Fact]
    public async Task HasHierarchyChanges_UnchangedNotificationCollection_DoesNotReadMissingSnapshotSlot()
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
    public async Task HasHierarchyChanges_NotificationCollectionChange_UsesForeignKeyModification()
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
