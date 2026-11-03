using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>A supplemental parent snapshot never hides later wrong-parent CLR membership.</summary>
    /// <param name="installed">Whether EF installed the planned Parent or it remains a raw marked intent.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupplementalParentCapturePreservesUnexpectedMembershipCleanup(
        bool installed
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var root = CreateNode(1);
        var expected = CreateNode(2);
        expected.ParentId = root.Id;
        expected.Parent = root;
        var moving = CreateNode(4);
        moving.ParentId = root.Id;
        moving.Parent = root;
        var wrong = CreateNode(3);
        wrong.ParentId = root.Id;
        wrong.Parent = root;
        var child = CreateNode(5);
        child.ParentId = wrong.Id;
        child.Parent = wrong;
        root.Children!.Add(expected);
        root.Children.Add(moving);
        root.Children.Add(wrong);
        wrong.Children!.Add(child);
        context.Attach(root);
        var movingEntry = context.Entry(moving);
        var parent = movingEntry.Property(node => node.ParentId);
        if (installed)
        {
            parent.CurrentValue = expected.Id;
        }
        else
        {
            // WHY: Raw marked Parent intent keeps the old native identity. It must not rely on supplemental
            // native-change selection to perform the independent wrong-parent cleanup.
            moving.ParentId = expected.Id;
            moving.Parent = expected;
            parent.IsModified = true;
            root.Children.Remove(moving);
            expected.Children!.Add(moving);
        }

        var childEntry = context.Entry(child);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var movingUpdate = UpdateEntry(context, moving);
        var expectedUpdate = UpdateEntry(context, expected);
        var wrongUpdate = UpdateEntry(context, wrong);
        var childUpdate = UpdateEntry(context, child);
        var parentNavigation = movingEntry.Metadata.FindNavigation(nameof(ProbeNode.Parent))!;
        var key = parentNavigation.ForeignKey;
        var inverse = key.PrincipalToDependent!;
        var expectedCollection = expected.Children!;
        var wrongCollection = wrong.Children!;
        var expectedDependents = updates
            .GetDependents(expectedUpdate, key)
            .ToArray();

        var nativeExpected =
            (expectedUpdate.GetRelationshipSnapshotValue(inverse) as IEnumerable<object> ?? []).ToArray();

        var installedParent = movingUpdate.GetRelationshipSnapshotValue(parent.Metadata);
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        // WHY: C is captured only through unchanged D; it does not initially contain the pending moved member A.
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        wrongCollection.Add(moving);
        var parentAfterRawAdd = moving.Parent;
        var keyAfterRawAdd = moving.ParentId;

        // Act
        snapshot.Restore();
        // WHY: Repeating recovery must not reintroduce the invalid member from a supplemental snapshot.
        snapshot.Restore();

        // Assert
        Assert.Same(expected, parentAfterRawAdd);
        Assert.Equal(expected.Id, keyAfterRawAdd);
        Assert.Same(expectedCollection, expected.Children);
        Assert.Same(wrongCollection, wrong.Children);
        Assert.Same(moving, Assert.Single(expected.Children!));
        Assert.Same(child, Assert.Single(wrong.Children!));
        Assert.Same(expected, moving.Parent);
        Assert.Equal(expected.Id, moving.ParentId);
        Assert.True(parent.IsModified);
        Assert.Equal(root.Id, parent.OriginalValue);
        Assert.Same(wrong, child.Parent);
        Assert.Equal(wrong.Id, child.ParentId);
        Assert.False(childEntry.Property(node => node.ParentId).IsModified);
        Assert.Same(expectedUpdate, updates.FindPrincipal(movingUpdate, key));
        Assert.Same(wrongUpdate, updates.FindPrincipal(childUpdate, key));
        Assert.Equal(expectedDependents, updates.GetDependents(expectedUpdate, key));
        Assert.Same(childUpdate, Assert.Single(updates.GetDependents(wrongUpdate, key)));
        var wrongNative = Assert.IsAssignableFrom<ISet<object>>(wrongUpdate.GetRelationshipSnapshotValue(inverse));

        Assert.True(nativeExpected
            .ToHashSet(ReferenceEqualityComparer.Instance)
            .SetEquals(expectedUpdate.GetRelationshipSnapshotValue(inverse) as IEnumerable<object> ?? []));
        Assert.Equal(installedParent, movingUpdate.GetRelationshipSnapshotValue(parent.Metadata));
        Assert.Same(child, Assert.Single(wrongNative));
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }
}
