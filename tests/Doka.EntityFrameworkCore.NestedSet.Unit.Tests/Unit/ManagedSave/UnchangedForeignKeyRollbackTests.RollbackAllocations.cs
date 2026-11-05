using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

// WHY: Complete recovery allocation probes use the existing serialized, uninstrumented observer collection.
[Collection("Allocation measurements")]
public sealed partial class UnchangedForeignKeyRollbackTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains complete recovery allocations beside the fixed width-dependent resource bounds.</summary>
    public UnchangedForeignKeyRollbackTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>The native snapshot stage scans one shared endpoint once for a complete captured FK set.</summary>
    /// <param name="nodes">The captured dependents sharing one ordinary owner.</param>
    [Trait("Category", "Allocation")]
    [Theory]
    [InlineData(128)]
    [InlineData(20000)]
    public async Task NativeOnlySharedOwnerRecoveryKeepsLinearAllocation(
        int nodes
    )
    {
        // Arrange
        await using var context = new RollbackContext();
        var owner = new RollbackOwner { Id = 2 };
        var transient = new RollbackOwner { Id = 1 };
        var members = Enumerable
            .Range(1, nodes)
            .Select(id => new RollbackNode
            {
                Id = id,
                OwnerId = owner.Id,
                Owner = owner,
            })
            .ToArray();

        owner.Nodes.AddRange(members);
        context.AttachRange(owner, transient);
        var node = members[nodes / 2];
        var entry = context.Entry(node);
        var ownerUpdate = Update(context, owner);
        var navigation = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!.Inverse!;
        var entries = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .ToArray();

        var warmup = CaptureRelationships();
        StageUnmarkedRoundTrip();
        warmup.PrepareRestore();
        warmup.RestoreSnapshots();

        // WHY: The before-repair profile loses this native member. Reset known fixture state outside the
        // measured interval so both versions start with identical actual membership and original CLR order.
        NestedSetInsertionContract.Instance.AddCollectionSnapshot(ownerUpdate, navigation, node);
        owner.Nodes.Remove(node);
        owner.Nodes.Insert(nodes / 2, node);
        var snapshot = CaptureRelationships();
        StageUnmarkedRoundTrip();
        var pendingOrder = owner.Nodes.ToArray();

        // WHY: This probe isolates indexed native/collection repair from complete scalar/flag replay.
        // Complete tracker capture and complete selected-entry recovery have their own unchanged budgets.
        snapshot.PrepareRestore();
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        snapshot.RestoreSnapshots();
        snapshot.Verify();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        // WHY: Native FK verification, endpoint comparison and exact-member repair must remain linear
        // instead of rescanning this owner for every dependent in the complete captured set.
        var maximum = 65_536L + nodes * 1536L;
        _output.WriteLine(
            $"Native snapshot stage: nodes={nodes}; entries={entries.Length}; "
            + $"allocated={allocated} B; maximum={maximum} B.");

        Assert.InRange(allocated, 1, maximum);
        Assert.Equal(nodes + 2, entries.Length);
        Assert.Equal(pendingOrder, owner.Nodes);
        Assert.True(
            Members(ownerUpdate, navigation)
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(members));

        Assert.Equal(owner.Id, node.OwnerId);
        Assert.Same(owner, node.Owner);
        Assert.False(
            entry.Property(value => value.OwnerId)
                .IsModified);

        Assert.Empty(transient.Nodes);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        return;

        // WHY: Capture the same FK/navigation preimages as the production tracker snapshot without
        // charging unrelated scalar replay to this native-index stage's resource boundary.
        NestedSetRelationshipSnapshot CaptureRelationships()
        {
            NestedSetRelationshipSnapshot? relationships = null;

            foreach (var candidate in entries)
            {
                NestedSetRelationshipSnapshot.Capture(context, candidate, ref relationships);
            }

            relationships!.CaptureParentNavigations();

            return relationships;
        }

        // WHY: A raw duplicate remains when EF removes the original occurrence, so returning the FK skips
        // inverse insertion even though its native set no longer contains the member.
        void StageUnmarkedRoundTrip()
        {
            owner.Nodes.Add(node);
            entry.Property(value => value.OwnerId)
                .CurrentValue = transient.Id;

            entry.Property(value => value.OwnerId)
                .CurrentValue = owner.Id;

            entry.Property(value => value.OwnerId)
                .IsModified = false;
        }
    }

    /// <summary>One selected dependent recovers a wide endpoint with bounded linear projection scratch.</summary>
    /// <param name="nodes">The actual original membership of the ordinary collection owner.</param>
    [Trait("Category", "Allocation")]
    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(2048)]
    [InlineData(20000)]
    public async Task SupplementalRecoveryKeepsLinearAllocation(
        int nodes
    )
    {
        // Arrange
        await using var context = new RollbackContext();
        var owner = new RollbackOwner { Id = 2 };
        var transient = new RollbackOwner { Id = 1 };
        var members = Enumerable
            .Range(1, nodes)
            .Select(id => new RollbackNode
            {
                Id = id,
                OwnerId = owner.Id,
                Owner = owner,
            })
            .ToArray();

        owner.Nodes.AddRange(members);
        context.AttachRange(owner, transient);
        var node = members[nodes / 2];
        var entry = context.Entry(node);
        node.Name = "Pending name";
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = Update(context, node);
        var ownerUpdate = Update(context, owner);
        var navigation = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!.Inverse!;
        var original = owner.Nodes.ToArray();

        // WHY: Warm the actual native move, projection, original restoration and verification on this graph.
        var warmup = NestedSetTrackerSnapshot.Capture(context, [update]);
        StageNativeMove(warmup);
        warmup.Restore();
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [update]);
        StageNativeMove(snapshot);
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        snapshot.Restore();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        // WHY: Includes the complete exceptional recovery, not just the tuple receipt. A repeated full
        // owner copy for every selected/original occurrence would exceed this fixed linear envelope.
        var maximum = 32_768L + nodes * 384L;
        _output.WriteLine(
            $"Supplemental recovery: width={nodes}; entries=1; allocated={allocated} B; "
            + $"maximum={maximum} B.");

        Assert.InRange(allocated, 1, maximum);
        Assert.Equal(original, owner.Nodes, ReferenceEqualityComparer.Instance);
        Assert.Empty(transient.Nodes);
        Assert.Equal(owner.Id, node.OwnerId);
        Assert.Same(owner, node.Owner);
        Assert.False(
            entry.Property(value => value.OwnerId)
                .IsModified);

        Assert.True(
            entry.Property(value => value.Name)
                .IsModified);

        Assert.Contains(node, Members(ownerUpdate, navigation));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        return;

        // WHY: Warmup and measurement use the same actual EF move and library membership installation.
        void StageNativeMove(
            NestedSetTrackerSnapshot target
        )
        {
            entry.Property(value => value.OwnerId)
                .CurrentValue = transient.Id;

            node.OwnerId = owner.Id;
            entry.Property(value => value.OwnerId)
                .IsModified = true;

            owner.Nodes.Add(node);
            target.RecordPersistedAddition(ownerUpdate, navigation, node);
            NestedSetInsertionContract.Instance.AddCollectionSnapshot(ownerUpdate, navigation, node);
        }
    }
}
