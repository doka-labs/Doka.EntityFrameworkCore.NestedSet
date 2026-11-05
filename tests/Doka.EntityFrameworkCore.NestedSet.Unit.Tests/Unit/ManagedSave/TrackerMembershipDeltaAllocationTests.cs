namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Bounds native-membership delta bookkeeping independently of an owner's existing collection width.</summary>
[Trait("Category", "Allocation")]
[Collection("Allocation measurements")]
public sealed class TrackerMembershipDeltaAllocationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Preserves the measured single-addition allocation beside its fixed resource budget.</summary>
    public TrackerMembershipDeltaAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>One actual native addition does not copy the owner's existing CLR or native members.</summary>
    /// <param name="width">The actual preexisting tracked membership of the ordinary collection owner.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(8192)]
    public async Task RecordingOneNativeAdditionHasFixedAllocation(
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

        // WHY: Entry snapshots and relationship eligibility are different contracts. An empty write set
        // isolates this bookkeeping budget while the owner and every existing member use real EF entries.
        var warmup = NestedSetTrackerSnapshot.Capture(context, []);
        warmup.RecordPersistedAddition(ownerEntry, navigation, member);
        var snapshot = NestedSetTrackerSnapshot.Capture(context, []);
        owner.ScopeNodes.Add(member);
        context.Attach(member);
        var nativeBefore = ownerEntry.GetRelationshipSnapshotValue(navigation) as ISet<object>;
        var countBefore = nativeBefore?.Count ?? 0;
        var containedBefore = nativeBefore?.Contains(member) == true;
        var contract = NestedSetInsertionContract.Instance;
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        snapshot.RecordPersistedAddition(ownerEntry, navigation, member);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        contract.AddCollectionSnapshot(ownerEntry, navigation, member);
        var installed = (ownerEntry.GetRelationshipSnapshotValue(navigation) as ISet<object>)?.Contains(member) == true;
        snapshot.Restore();

        // Assert
        // WHY: One lazy tuple-list allocation fits this fixed allowance; copying an 8,192-member owner
        // requires tens of kilobytes and must fail even though one-member owners would hide that growth.
        const long maximum = 4096;
        _output.WriteLine(
            $"Native membership delta: width={width}; additions=1; allocated={allocated} B; "
            + $"maximum={maximum} B.");

        Assert.InRange(allocated, 0, maximum);
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

        GC.KeepAlive(snapshot);
    }
}
