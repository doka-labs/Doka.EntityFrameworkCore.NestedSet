using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Rollback preserves notification graphs and pending payload after wrong-target fixup.</summary>
    [Fact]
    public async Task NotificationRollbackRestoresAuthorizedMoveAndModifiedUnexpectedParent()
    {
        // Arrange
        await using var context = new NotificationContext();
        context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
        var root = CreateNode();
        root.Name = "Root";
        root.Right = 12;
        var expected = CreateNode(2);
        expected.Name = "Expected";
        expected.ParentId = root.Id;
        expected.Parent = root;
        expected.Left = 2;
        expected.Right = 3;
        expected.Depth = 1;
        var moving = CreateNode(4);
        moving.Name = "Moving";
        moving.ParentId = root.Id;
        moving.Parent = root;
        moving.Left = 4;
        moving.Right = 5;
        moving.Depth = 1;
        moving.Position = 1;
        var unexpected = CreateNode(3);
        unexpected.Name = "Unexpected";
        unexpected.ParentId = root.Id;
        unexpected.Parent = root;
        unexpected.Left = 6;
        unexpected.Right = 11;
        unexpected.Depth = 1;
        unexpected.Position = 2;
        var first = CreateNode(5);
        first.Name = "First";
        first.ParentId = unexpected.Id;
        first.Parent = unexpected;
        first.Left = 7;
        first.Right = 8;
        first.Depth = 2;
        var second = CreateNode(6);
        second.Name = "Second";
        second.ParentId = unexpected.Id;
        second.Parent = unexpected;
        second.Left = 9;
        second.Right = 10;
        second.Depth = 2;
        second.Position = 1;
        root.Children!.Add(expected);
        root.Children.Add(moving);
        root.Children.Add(unexpected);
        unexpected.Children!.Add(first);
        unexpected.Children.Add(second);
        context.Attach(root);

        // WHY: Real notification fixup installs the authorized target before capture. The wrong target is
        // already Modified for ordinary payload, so rollback cannot assume all unexpected owners are Unchanged.
        moving.Parent = expected;
        unexpected.Payload = "Pending application payload";
        var movingEntry = context.Entry(moving);
        var unexpectedEntry = context.Entry(unexpected);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var movingUpdate = UpdateEntry(context, moving);
        var rootUpdate = UpdateEntry(context, root);
        var expectedUpdate = UpdateEntry(context, expected);
        var unexpectedUpdate = UpdateEntry(context, unexpected);
        var parentNavigation = movingEntry.Metadata.FindNavigation(nameof(ProbeNode.Parent))!;
        var parentKey = parentNavigation.ForeignKey;
        var parentProperty = movingEntry
            .Property(value => value.ParentId)
            .Metadata;

        var scopeProperty = movingEntry
            .Property(value => value.Scope)
            .Metadata;

        var idProperty = movingEntry
            .Property(value => value.Id)
            .Metadata;

        var rootCollection = root.Children;
        var expectedCollection = expected.Children;
        var unexpectedCollection = unexpected.Children!;
        var authorizedPrincipal = updates.FindPrincipal(movingUpdate, parentKey);
        var authorizedInstalledParent = movingUpdate.GetRelationshipSnapshotValue(parentProperty);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        unexpectedCollection.Insert(1, moving);
        var callbackParent = moving.Parent;
        var callbackParentId = moving.ParentId;
        var callbackBucket = updates
            .GetDependents(unexpectedUpdate, parentKey)
            .Select(entry => entry.GetCurrentValue<int>(idProperty))
            .OrderBy(id => id)
            .ToArray();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(expectedUpdate, authorizedPrincipal);
        Assert.Equal(expected.Id, authorizedInstalledParent);
        Assert.Same(unexpected, callbackParent);
        Assert.Equal(unexpected.Id, callbackParentId);
        Assert.Equal([moving.Id, first.Id, second.Id], callbackBucket);
        Assert.Same(expected, moving.Parent);
        Assert.Equal(expected.Id, moving.ParentId);
        Assert.Same(rootCollection, root.Children);
        Assert.Same(expectedCollection, expected.Children);
        Assert.Same(unexpectedCollection, unexpected.Children);
        Assert.Collection(root.Children!, node => Assert.Same(expected, node), node => Assert.Same(unexpected, node));
        Assert.Same(moving, Assert.Single(expected.Children!));
        Assert.Collection(unexpected.Children!, node => Assert.Same(first, node), node => Assert.Same(second, node));
        Assert.Same(expectedUpdate, updates.FindPrincipal(movingUpdate, parentKey));
        Assert.Same(expected, movingUpdate.GetRelationshipSnapshotValue(parentNavigation));
        Assert.Equal(expected.Id, movingUpdate.GetRelationshipSnapshotValue(parentProperty));
        Assert.Equal(moving.Scope, movingUpdate.GetRelationshipSnapshotValue(scopeProperty));
        Assert.Same(movingUpdate, Assert.Single(updates.GetDependents(expectedUpdate, parentKey)));
        Assert.Equal(
            [expected.Id, unexpected.Id],
            updates
                .GetDependents(rootUpdate, parentKey)
                .Select(entry => entry.GetCurrentValue<int>(idProperty))
                .OrderBy(id => id));

        Assert.Equal(
            [first.Id, second.Id],
            updates
                .GetDependents(unexpectedUpdate, parentKey)
                .Select(entry => entry.GetCurrentValue<int>(idProperty))
                .OrderBy(id => id));

        Assert.Equal(EntityState.Modified, movingEntry.State);
        Assert.True(
            movingEntry.Property(value => value.ParentId)
                .IsModified);

        Assert.Equal(
            root.Id,
            movingEntry.Property(value => value.ParentId)
                .OriginalValue);

        Assert.False(
            movingEntry.Property(value => value.Name)
                .IsModified);

        Assert.Equal(EntityState.Modified, unexpectedEntry.State);
        Assert.True(
            unexpectedEntry.Property(value => value.Payload)
                .IsModified);

        Assert.Equal("Pending application payload", unexpected.Payload);
        Assert.False(
            unexpectedEntry.Property(value => value.ParentId)
                .IsModified);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(root)
                .State);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(expected)
                .State);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(first)
                .State);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(second)
                .State);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(CascadeTiming.OnSaveChanges, context.ChangeTracker.CascadeDeleteTiming);
        Assert.Equal(CascadeTiming.Never, context.ChangeTracker.DeleteOrphansTiming);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
