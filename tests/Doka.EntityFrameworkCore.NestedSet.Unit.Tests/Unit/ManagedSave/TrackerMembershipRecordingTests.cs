namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks native-membership delta recovery against independently retained owner members.</summary>
public sealed class TrackerMembershipRecordingTests
{
    /// <summary>One actual native addition restores only its membership and preserves every original owner member.</summary>
    /// <param name="width">The actual preexisting tracked membership of the ordinary collection owner.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(8192)]
    public async Task RecordPersistedAddition_OneNativeAddition_RestoresOnlyItsMembership(
        int width
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var owner = new HierarchyChangeDetectionTests.ProbeOwner { Id = 7 };
        var existing = Enumerable
            .Range(1, width)
            .Select(id =>
            {
                var node = HierarchyChangeDetectionTests.CreateNode(id);
                node.TreeId = Guid.NewGuid();

                return node;
            })
            .ToArray();

        foreach (var node in existing)
        {
            owner.ScopeNodes.Add(node);
        }

        context.Attach(owner);
        var ownerEntry = HierarchyChangeDetectionTests.UpdateEntry(context, owner);
        var navigation = ownerEntry.EntityType.FindNavigation(nameof(owner.ScopeNodes))!;
        var collection = owner.ScopeNodes;
        var member = HierarchyChangeDetectionTests.CreateNode(width + 1);
        member.TreeId = Guid.NewGuid();

        // WHY: An empty write set isolates membership recovery while every existing member has a real EF identity.
        var snapshot = NestedSetTrackerSnapshot.Capture(context, []);
        owner.ScopeNodes.Add(member);
        context.Attach(member);
        var nativeBefore = ownerEntry.GetRelationshipSnapshotValue(navigation) as ISet<object>;
        var countBefore = nativeBefore?.Count ?? 0;
        var containedBefore = nativeBefore?.Contains(member) == true;
        var contract = NestedSetInsertionContract.Instance;

        // Act
        snapshot.RecordPersistedAddition(ownerEntry, navigation, member);
        contract.AddCollectionSnapshot(ownerEntry, navigation, member);
        var installed = (ownerEntry.GetRelationshipSnapshotValue(navigation) as ISet<object>)?.Contains(member) == true;
        snapshot.Restore();

        // Assert
        Assert.Equal(width, countBefore);
        Assert.False(containedBefore);
        Assert.True(installed);
        Assert.Same(collection, owner.ScopeNodes);
        Assert.Equal(width + 1, owner.ScopeNodes.Count);
        Assert.Contains(member, owner.ScopeNodes);
        var nativeAfter = Assert.IsAssignableFrom<ISet<object>>(ownerEntry.GetRelationshipSnapshotValue(navigation));

        Assert.Equal(width, nativeAfter.Count);
        Assert.DoesNotContain(member, nativeAfter);
        Assert.All(existing, node => Assert.Contains(node, nativeAfter));
        Assert.Equal(EntityState.Unchanged, ownerEntry.EntityState);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
