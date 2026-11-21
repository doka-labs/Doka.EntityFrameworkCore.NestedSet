using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnchangedForeignKeyRollbackTests
{
    /// <summary>The native snapshot stage scans one shared endpoint once for a complete captured FK set.</summary>
    /// <param name="nodes">The captured dependents sharing one ordinary owner.</param>
    [Theory]
    [InlineData(128)]
    [InlineData(20000)]
    public async Task RestoreSnapshots_NativeOnlySharedOwnerRecovery_RestoresMembership(
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

        var snapshot = CaptureRelationships();
        StageUnmarkedRoundTrip();
        var pendingOrder = owner.Nodes.ToArray();

        // WHY: This control isolates indexed native/collection repair from complete scalar/flag replay.
        snapshot.PrepareRestore();

        // Act
        snapshot.RestoreSnapshots();
        snapshot.Verify();

        // Assert
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

        // WHY: Capture the same FK/navigation preimages as the production tracker snapshot while
        // keeping scalar replay separate from this native-membership control.
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

    /// <summary>One selected dependent recovers a wide endpoint with exact membership and pending scalar flags.</summary>
    /// <param name="nodes">The actual original membership of the ordinary collection owner.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(2048)]
    [InlineData(20000)]
    public async Task Restore_SupplementalRecovery_RestoresOriginalEndpoint(
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

        var snapshot = NestedSetTrackerSnapshot.Capture(context, [update]);
        StageNativeMove(snapshot);

        // Act
        snapshot.Restore();

        // Assert
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

        // WHY: Stage the accepted native move and persisted membership before restoring the pending payload.
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
