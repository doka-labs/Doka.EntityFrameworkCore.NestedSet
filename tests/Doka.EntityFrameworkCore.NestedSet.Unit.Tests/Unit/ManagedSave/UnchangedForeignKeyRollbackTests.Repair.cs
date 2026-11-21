using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnchangedForeignKeyRollbackTests
{
    /// <summary>Composite repair cannot steal a unique dependent at a mixed old/new key tuple.</summary>
    /// <param name="originalScope">Whether the occupied hybrid uses the original scope or original ID.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_CompositeRepair_PreservesUncapturedUniqueIntermediateVictim(
        bool originalScope
    )
    {
        // Arrange
        await using var context = new UniqueRollbackContext();
        var original = new RollbackOwner
        {
            Id = 2,
            Scope = 7,
        };

        var transient = new RollbackOwner
        {
            Id = 1,
            Scope = 9,
        };

        var intermediate = new RollbackOwner
        {
            Id = originalScope
                ? transient.Id
                : original.Id,
            Scope = originalScope
                ? original.Scope
                : transient.Scope,
        };

        var node = new RollbackNode
        {
            Id = 20,
            OwnerScope = original.Scope,
            OwnerId = original.Id,
            Owner = original,
        };

        var victim = new RollbackNode
        {
            Id = 99,
            OwnerScope = intermediate.Scope,
            OwnerId = intermediate.Id,
            Owner = intermediate,
        };

        original.UniqueNode = node;
        intermediate.UniqueNode = victim;
        context.AttachRange(original, transient, intermediate);
        var entry = context.Entry(node);
        var victimEntry = context.Entry(victim);
        node.Name = "Pending name";
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = Update(context, node);
        var victimUpdate = Update(context, victim);
        var originalUpdate = Update(context, original);
        var transientUpdate = Update(context, transient);
        var intermediateUpdate = Update(context, intermediate);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var reference = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!;
        var key = reference.ForeignKey;
        var installedBefore = Installed(update, key);
        var victimInstalled = Installed(victimUpdate, key);

        // WHY: Only the actual candidate belongs to this snapshot. A wrong intermediate traversal cannot
        // rely on another entry snapshot to hide native victim-stealing or restore its lost unique reference.
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [update]);

        // WHY: The callback must leave the victim intact. Reference assignment itself traverses mixed tuples;
        // select the public scalar order that avoids the occupied hybrid so only rollback is under test.
        if (originalScope)
        {
            entry.Property(value => value.OwnerScope)
                .CurrentValue = transient.Scope;

            entry.Property(value => value.OwnerId)
                .CurrentValue = transient.Id;
        }
        else
        {
            entry.Property(value => value.OwnerId)
                .CurrentValue = transient.Id;

            entry.Property(value => value.OwnerScope)
                .CurrentValue = transient.Scope;
        }

        node.OwnerScope = original.Scope;
        node.OwnerId = original.Id;
        entry.Property(value => value.OwnerScope)
            .IsModified = false;

        entry.Property(value => value.OwnerId)
            .IsModified = false;

        var victimAfterCallback = Installed(victimUpdate, key);
        var hiddenInstalled = Installed(update, key);

        // Act
        snapshot.Restore();

        // Assert
        Assert.True(key.IsUnique);
        Assert.Equal(victimInstalled, victimAfterCallback);
        Assert.Equal(key.PrincipalKey.Properties.Select(transientUpdate.GetCurrentValue), hiddenInstalled);
        Assert.Equal(installedBefore, Installed(update, key));
        Assert.Same(original, node.Owner);
        Assert.Same(node, original.UniqueNode);
        Assert.Null(transient.UniqueNode);
        Assert.Contains(update, updates.GetDependents(originalUpdate, key));
        Assert.DoesNotContain(update, updates.GetDependents(transientUpdate, key));
        Assert.Equal(victimInstalled, Installed(victimUpdate, key));
        Assert.Equal(intermediate.Scope, victim.OwnerScope);
        Assert.Equal(intermediate.Id, victim.OwnerId);
        Assert.Same(intermediate, victim.Owner);
        Assert.Same(victim, intermediate.UniqueNode);
        Assert.Contains(victimUpdate, updates.GetDependents(intermediateUpdate, key));
        Assert.Same(intermediate, victimUpdate.GetRelationshipSnapshotValue(reference));
        Assert.Equal(EntityState.Unchanged, victimEntry.State);
        Assert.All(key.Properties, property => Assert.False(victimUpdate.IsModified(property)));
        Assert.False(
            victimEntry.Property(value => value.Name)
                .IsModified);

        Assert.True(
            entry.Property(value => value.Name)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>
    ///     Explicit FK write intent restores a completed native round trip without detecting raw reorders.
    /// </summary>
    /// <param name="mode">The explicit public write, raw reorder or deliberately unmarked round trip.</param>
    [Theory]
    [InlineData("public")]
    [InlineData("raw")]
    [InlineData("public-unmarked")]
    public async Task Restore_NativeRoundTrip_RespectsExplicitAndUnmarkedIntent(
        string mode
    )
    {
        // Arrange
        await using var context = new RollbackContext();
        var original = new RollbackOwner { Id = 2 };
        var transient = new RollbackOwner { Id = 1 };
        var members = Enumerable
            .Range(1, 3)
            .Select(id => new RollbackNode
            {
                Id = id,
                OwnerId = original.Id,
                Owner = original,
            })
            .ToArray();

        original.Nodes.AddRange(members);
        context.AttachRange(original, transient);
        var node = members[0];
        var entry = context.Entry(node);
        var property = entry.Property(value => value.OwnerId);
        var update = Update(context, node);
        var owner = Update(context, original);
        var key = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!.ForeignKey;
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [update]);

        if (mode == "raw")
        {
            original.Nodes.Remove(node);
            original.Nodes.Add(node);
        }
        else
        {
            property.CurrentValue = transient.Id;
            property.CurrentValue = original.Id;

            if (mode == "public-unmarked")
            {
                // WHY: Explicitly clearing the write flag leaves the same current/native state as a raw
                // reorder. Recovery observes the remaining write intent rather than guessing callback history.
                property.IsModified = false;
            }
        }

        var written = property.IsModified;
        var pendingOrder = original.Nodes.ToArray();

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(mode == "public", written);
        Assert.Equal([members[1], members[2], members[0]], pendingOrder);
        Assert.Equal(
            mode == "public"
                ? members
                : pendingOrder,
            original.Nodes,
            ReferenceEqualityComparer.Instance);

        Assert.Same(original, node.Owner);
        Assert.Equal(original.Id, node.OwnerId);
        Assert.False(property.IsModified);
        Assert.Empty(transient.Nodes);
        Assert.True(
            Members(owner, key.PrincipalToDependent!)
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(members));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Full FK preimages retain a generated principal's original temporary relationship sidecars.</summary>
    /// <param name="allEntries">Whether the snapshot owns all entries or only the affected hierarchy node.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_TemporaryPrincipalRelationship_RetainsExactSidecars(
        bool allEntries
    )
    {
        // Arrange
        await using var context = new GeneratedRollbackContext();
        var original = new RollbackOwner { Id = 2 };
        context.Attach(original);
        var node = new RollbackNode
        {
            Id = 20,
            OwnerId = original.Id,
            Owner = original,
        };

        var entry = context.Attach(node);
        var generated = new RollbackOwner();
        var generatedEntry = await context.AddAsync(generated, CancellationToken.None);
        entry.Reference(value => value.Owner)
            .CurrentValue = generated;

        node.Name = "Pending name";
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = Update(context, node);
        var generatedUpdate = Update(context, generated);
        var property = entry.Property(value => value.OwnerId);
        var generatedProperty = generatedEntry.Property(value => value.Id);
        var reference = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!;
        var key = reference.ForeignKey;
        var valuesBefore = Installed(update, key);
        var currentBefore = property.CurrentValue;
        var originalBefore = property.OriginalValue;
        var clrBefore = node.OwnerId;
        var temporaryBefore = property.IsTemporary;
        var principalTemporaryBefore = generatedProperty.IsTemporary;
        var snapshot = allEntries
            ? NestedSetTrackerSnapshot.Capture(context)
            : NestedSetTrackerSnapshot.Capture(context, [update]);

        node.Name = "Callback name";
        entry.Property(value => value.Name)
            .OriginalValue = "Callback original";

        // Act
        snapshot.Restore();

        // Assert
        Assert.True(temporaryBefore);
        Assert.True(principalTemporaryBefore);
        Assert.Equal(currentBefore, property.CurrentValue);
        Assert.Equal(originalBefore, property.OriginalValue);
        Assert.Equal(clrBefore, node.OwnerId);
        Assert.True(property.IsTemporary);
        Assert.True(generatedProperty.IsTemporary);
        Assert.Equal(valuesBefore, Installed(update, key));
        Assert.Same(generated, node.Owner);
        Assert.Same(generated, update.GetRelationshipSnapshotValue(reference));
        Assert.Contains(
            update,
            context
                .GetService<IUpdateAdapterFactory>()
                .Create()
                .GetDependents(generatedUpdate, key));

        Assert.Same(node, Assert.Single(generated.Nodes));
        Assert.Equal(EntityState.Added, generatedEntry.State);
        Assert.Equal("Pending name", node.Name);
        Assert.Equal(
            "Before",
            entry.Property(value => value.Name)
                .OriginalValue);

        Assert.True(
            entry.Property(value => value.Name)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
