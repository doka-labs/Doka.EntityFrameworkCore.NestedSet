using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves initially unchanged FK identities independently of raw callback scalar resets.</summary>
public sealed partial class UnchangedForeignKeyRollbackTests
{
    /// <summary>An initially unchanged FK restores its exact native maps, references and pending CLR graph.</summary>
    /// <param name="composite">Whether ordinary FKs share a composite scope member.</param>
    /// <param name="originallyNull">Whether the nullable ordinary relationship initially has no owner.</param>
    /// <param name="marked">Whether the callback marks the raw reset as an ordinary persisted FK write.</param>
    /// <param name="allEntries">Whether the snapshot captures the whole tracker or its selected write entry.</param>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public async Task UnchangedForeignKeyRestoresInstalledMapsAndPendingGraph(
        bool composite,
        bool originallyNull,
        bool marked,
        bool allEntries
    )
    {
        // Arrange
        await using var context = composite ? new CompositeRollbackContext() : new RollbackContext();
        var original = new RollbackOwner { Id = 2 };
        var transient = new RollbackOwner
        {
            Id = 1,
            Scope = 9,
        };

        var reviewer = new RollbackOwner { Id = 4 };
        var first = new RollbackNode
        {
            Id = 10,
            OwnerId = original.Id,
            Owner = original,
        };

        var last = new RollbackNode
        {
            Id = 30,
            OwnerId = original.Id,
            Owner = original,
        };

        var node = new RollbackNode
        {
            Id = 20,
            OwnerId = originallyNull ? null : original.Id,
            Owner = originallyNull ? null : original,
            ReviewerId = composite ? reviewer.Id : null,
            Reviewer = composite ? reviewer : null,
        };

        original.Nodes.Add(first);

        if (!originallyNull)
        {
            original.Nodes.Add(node);
        }

        original.Nodes.Add(last);
        context.AttachRange(original, transient, reviewer);
        var entry = context.Attach(node);

        if (!originallyNull)
        {
            // WHY: A raw ordered list may contain duplicate references while EF stores one native member.
            // Rollback must restore the caller's exact pending order, independently of that native set.
            original.Nodes.Remove(node);
            original.Nodes.Insert(0, node);
            original.Nodes.Add(node);
        }

        node.Name = "Pending name";
        entry.Property(value => value.Name).IsModified = true;
        var update = Update(context, node);
        var originalUpdate = Update(context, original);
        var transientUpdate = Update(context, transient);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var reference = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!;
        var key = reference.ForeignKey;
        var inverse = key.PrincipalToDependent!;
        var installedBefore = Installed(update, key);
        var currentBefore = key
            .Properties
            .Select(update.GetCurrentValue)
            .ToArray();

        var modifiedBefore = key
            .Properties
            .Select(update.IsModified)
            .ToArray();

        var originalsBefore = key
            .Properties
            .Select(update.GetOriginalValue)
            .ToArray();

        var referenceBefore = node.Owner;
        var installedReferenceBefore = update.GetRelationshipSnapshotValue(reference);
        var originalCollection = original.Nodes;
        var originalMembers = original.Nodes.ToArray();
        var transientCollection = transient.Nodes;
        var transientMembers = transient.Nodes.ToArray();
        var nativeOriginal = Members(originalUpdate, inverse);
        var nativeTransient = Members(transientUpdate, inverse);
        var reviewerMembers = reviewer.ReviewNodes.ToArray();
        var snapshot = allEntries
            ? NestedSetTrackerSnapshot.Capture(context)
            : NestedSetTrackerSnapshot.Capture(context, [update]);

        entry.Reference(value => value.Owner).CurrentValue = transient;
        node.OwnerId = originallyNull ? null : original.Id;
        node.OwnerScope = original.Scope;
        entry.Property(value => value.OwnerId).IsModified = marked;

        if (composite)
        {
            entry.Property(value => value.OwnerScope).IsModified = marked;
        }

        var hiddenInstalled = Installed(update, key);
        var hiddenBucket = updates
            .GetDependents(transientUpdate, key)
            .Contains(update);

        if (marked && !originallyNull)
        {
            if (!original.Nodes.Contains(node))
            {
                original.Nodes.Add(node);
            }

            snapshot.RecordPersistedAddition(originalUpdate, inverse, node);
            NestedSetInsertionContract.Instance.AddCollectionSnapshot(originalUpdate, inverse, node);
        }

        // Act
        snapshot.Restore();
        // WHY: A later cleanup attempt must not replay an already retired inverse delta over the restored
        // original member. Both restores belong to this same idempotent rollback contract.
        snapshot.Restore();

        // Assert
        Assert.Equal(installedBefore, currentBefore);
        Assert.Equal(originalsBefore, currentBefore);
        Assert.All(modifiedBefore, modified => Assert.False(modified));
        Assert.True(hiddenBucket);
        Assert.Equal(key.PrincipalKey.Properties.Select(transientUpdate.GetCurrentValue), hiddenInstalled);
        Assert.Equal(installedBefore, Installed(update, key));
        Assert.Equal(originalsBefore, key.Properties.Select(update.GetOriginalValue));
        Assert.Equal(installedBefore, key.Properties.Select(update.GetCurrentValue));
        Assert.Same(referenceBefore, node.Owner);
        Assert.Same(installedReferenceBefore, update.GetRelationshipSnapshotValue(reference));
        Assert.Same(originalCollection, original.Nodes);
        Assert.Equal(originalMembers, original.Nodes, ReferenceEqualityComparer.Instance);
        Assert.Same(transientCollection, transient.Nodes);
        Assert.Equal(transientMembers, transient.Nodes, ReferenceEqualityComparer.Instance);
        Assert.True(nativeOriginal
            .ToHashSet(ReferenceEqualityComparer.Instance)
            .SetEquals(Members(originalUpdate, inverse)));
        Assert.True(nativeTransient
            .ToHashSet(ReferenceEqualityComparer.Instance)
            .SetEquals(Members(transientUpdate, inverse)));
        Assert.Equal(!originallyNull,
            updates
                .GetDependents(originalUpdate, key)
                .Contains(update));
        Assert.DoesNotContain(update, updates.GetDependents(transientUpdate, key));
        Assert.All(key.Properties, property => Assert.False(update.IsModified(property)));
        Assert.Equal("Pending name", node.Name);
        Assert.Equal("Before",
            entry.Property(value => value.Name).OriginalValue);
        Assert.True(entry.Property(value => value.Name).IsModified);
        Assert.Equal(EntityState.Modified, entry.State);

        if (composite)
        {
            var reviewerReference = entry.Metadata.FindNavigation(nameof(RollbackNode.Reviewer))!;
            var reviewerUpdate = Update(context, reviewer);

            Assert.Same(reviewer, node.Reviewer);
            Assert.Same(reviewer, update.GetRelationshipSnapshotValue(reviewerReference));
            Assert.Contains(update, updates.GetDependents(reviewerUpdate, reviewerReference.ForeignKey));
            Assert.Equal(reviewerMembers, reviewer.ReviewNodes, ReferenceEqualityComparer.Instance);
        }

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>A matching restored FK cannot invent native membership absent before the operation.</summary>
    /// <param name="composite">Whether ordinary FKs share a composite scope member.</param>
    /// <param name="allEntries">Whether the snapshot captures all entries or only the hierarchy candidate.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OriginallyAbsentMembershipRemainsAbsentDespiteMatchingRestoredForeignKey(
        bool composite,
        bool allEntries
    )
    {
        // Arrange
        await using var context = composite ? new CompositeRollbackContext() : new RollbackContext();
        var original = new RollbackOwner { Id = 2 };
        var transient = new RollbackOwner
        {
            Id = 1,
            Scope = 9,
        };

        context.AttachRange(original, transient);
        var node = new RollbackNode
        {
            Id = 20,
            OwnerId = original.Id,
        };

        original.Nodes.Add(node);
        var entry = context.Attach(node);
        node.Name = "Pending name";
        entry.Property(value => value.Name).IsModified = true;
        var update = Update(context, node);
        var originalUpdate = Update(context, original);
        var transientUpdate = Update(context, transient);
        var reference = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!;
        var key = reference.ForeignKey;
        var inverse = key.PrincipalToDependent!;
        var nativeBefore = Members(originalUpdate, inverse);
        var installedBefore = Installed(update, key);
        var modifiedBefore = key
            .Properties
            .Select(update.IsModified)
            .ToArray();

        var collection = original.Nodes;
        var snapshot = allEntries
            ? NestedSetTrackerSnapshot.Capture(context)
            : NestedSetTrackerSnapshot.Capture(context, [update]);

        entry.Reference(value => value.Owner).CurrentValue = transient;
        node.OwnerId = original.Id;
        node.OwnerScope = original.Scope;
        entry.Property(value => value.OwnerId).IsModified = true;

        if (composite)
        {
            entry.Property(value => value.OwnerScope).IsModified = true;
        }

        original.Nodes.Add(node);
        snapshot.RecordPersistedAddition(originalUpdate, inverse, node);
        NestedSetInsertionContract.Instance.AddCollectionSnapshot(originalUpdate, inverse, node);
        var installedAddition = Members(originalUpdate, inverse)
            .Contains(node);

        // Act
        snapshot.Restore();

        // Assert
        Assert.All(modifiedBefore, modified => Assert.False(modified));
        Assert.DoesNotContain(node, nativeBefore);
        Assert.True(installedAddition);
        Assert.Equal(installedBefore, Installed(update, key));
        Assert.Same(original, node.Owner);
        Assert.Same(original, update.GetRelationshipSnapshotValue(reference));
        Assert.DoesNotContain(node, Members(originalUpdate, inverse));
        Assert.DoesNotContain(node, Members(transientUpdate, inverse));
        Assert.Same(collection, original.Nodes);
        Assert.Same(node, Assert.Single(original.Nodes));
        Assert.Empty(transient.Nodes);
        Assert.False(entry.Property(value => value.OwnerId).IsModified);
        Assert.True(entry.Property(value => value.Name).IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Composite repair cannot steal a unique dependent at a mixed old/new key tuple.</summary>
    /// <param name="originalScope">Whether the occupied hybrid uses the original scope or original ID.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompositeRepairPreservesUncapturedUniqueIntermediateVictim(
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
            Id = originalScope ? transient.Id : original.Id,
            Scope = originalScope ? original.Scope : transient.Scope,
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
        entry.Property(value => value.Name).IsModified = true;
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
            entry.Property(value => value.OwnerScope).CurrentValue = transient.Scope;
            entry.Property(value => value.OwnerId).CurrentValue = transient.Id;
        }
        else
        {
            entry.Property(value => value.OwnerId).CurrentValue = transient.Id;
            entry.Property(value => value.OwnerScope).CurrentValue = transient.Scope;
        }

        node.OwnerScope = original.Scope;
        node.OwnerId = original.Id;
        entry.Property(value => value.OwnerScope).IsModified = false;
        entry.Property(value => value.OwnerId).IsModified = false;
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
        Assert.False(victimEntry.Property(value => value.Name).IsModified);
        Assert.True(entry.Property(value => value.Name).IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Supplemental repair restores selected duplicates without discarding unrelated raw list edits.</summary>
    [Fact]
    public async Task SelectedDuplicateAnchorsPreserveUnselectedPendingAddsAndRemovals()
    {
        // Arrange
        await using var context = new RollbackContext();
        var original = new RollbackOwner { Id = 2 };
        var transient = new RollbackOwner { Id = 1 };
        var first = new RollbackNode
        {
            Id = 10,
            OwnerId = original.Id,
            Owner = original,
        };

        var node = new RollbackNode
        {
            Id = 20,
            OwnerId = original.Id,
            Owner = original,
        };

        var last = new RollbackNode
        {
            Id = 30,
            OwnerId = original.Id,
            Owner = original,
        };

        var alias = new RollbackNode
        {
            Id = 40,
            OwnerId = transient.Id,
            Owner = transient,
        };

        original.Nodes.AddRange([node, first, last]);
        transient.Nodes.Add(alias);
        context.AttachRange(original, transient);
        original.Nodes.Add(node);
        var collection = original.Nodes;
        var entry = context.Entry(node);
        node.Name = "Pending name";
        entry.Property(value => value.Name).IsModified = true;
        var update = Update(context, node);
        var originalUpdate = Update(context, original);
        var transientUpdate = Update(context, transient);
        var key = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!.ForeignKey;
        var inverse = key.PrincipalToDependent!;
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [update]);
        entry.Reference(value => value.Owner).CurrentValue = transient;
        node.OwnerId = original.Id;
        entry.Property(value => value.OwnerId).IsModified = true;
        // WHY: These ordinary raw edits have no native FK effect. They remain application-owned, while
        // the moved candidate's two original occurrences retain their surviving anchor on either side.
        original.Nodes.Remove(first);
        original.Nodes.Insert(0, alias);
        var pendingUnselected = original
            .Nodes
            .Where(value => !ReferenceEquals(value, node))
            .ToArray();

        snapshot.RecordPersistedAddition(originalUpdate, inverse, node);
        NestedSetInsertionContract.Instance.AddCollectionSnapshot(originalUpdate, inverse, node);

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Same(collection, original.Nodes);
        Assert.Equal(pendingUnselected,
            original.Nodes.Where(value => !ReferenceEquals(value, node)),
            ReferenceEqualityComparer.Instance);
        Assert.Equal(2, original.Nodes.Count(value => ReferenceEquals(value, node)));
        var anchor = original.Nodes.IndexOf(last);

        Assert.True(anchor > 0 && anchor < original.Nodes.Count - 1);
        Assert.Same(node, original.Nodes[anchor - 1]);
        Assert.Same(node, original.Nodes[anchor + 1]);
        Assert.DoesNotContain(first, original.Nodes);
        Assert.Contains(alias, original.Nodes);
        Assert.Contains(first, Members(originalUpdate, inverse));
        Assert.Contains(node, Members(originalUpdate, inverse));
        Assert.DoesNotContain(alias, Members(originalUpdate, inverse));
        Assert.Contains(alias, Members(transientUpdate, inverse));
        Assert.DoesNotContain(node, Members(transientUpdate, inverse));
        Assert.Same(original, node.Owner);
        Assert.Same(transient, alias.Owner);
        Assert.Equal(transient.Id, alias.OwnerId);
        Assert.Equal(EntityState.Unchanged,
            context.Entry(alias).State);
        Assert.Equal(EntityState.Unchanged,
            context.Entry(first).State);
        Assert.False(entry.Property(value => value.OwnerId).IsModified);
        Assert.True(entry.Property(value => value.Name).IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Conflicting raw anchor order cannot reverse selected original dependent occurrences.</summary>
    /// <param name="reverseAnchors">Whether unrelated raw members are reordered after native fixup.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleSelectedMembersKeepOriginalOrderBesidePendingAnchors(
        bool reverseAnchors
    )
    {
        // Arrange
        await using var context = new RollbackContext();
        var original = new RollbackOwner { Id = 2 };
        var transient = new RollbackOwner { Id = 1 };
        var members = Enumerable
            .Range(1, 4)
            .Select(id => new RollbackNode
            {
                Id = id,
                OwnerId = original.Id,
                Owner = original,
            })
            .ToArray();

        original.Nodes.AddRange(members);
        context.AttachRange(original, transient);
        var first = members[0];
        var second = members[2];
        var selected = new[] { first, second };
        var owner = Update(context, original);
        var key = context
            .Entry(first)
            .Metadata
            .FindNavigation(nameof(RollbackNode.Owner))!.ForeignKey;

        var snapshot = NestedSetTrackerSnapshot.Capture(context, selected.Select(node => Update(context, node)));

        foreach (var node in selected)
        {
            context
                .Entry(node)
                .Property(value => value.OwnerId)
                .CurrentValue = transient.Id;
            node.OwnerId = original.Id;
            context
                .Entry(node)
                .Property(value => value.OwnerId)
                .IsModified = true;
            original.Nodes.Add(node);
            snapshot.RecordPersistedAddition(owner, key.PrincipalToDependent!, node);
            NestedSetInsertionContract.Instance.AddCollectionSnapshot(owner, key.PrincipalToDependent!, node);
        }

        if (reverseAnchors)
        {
            // WHY: Both selected and unselected reference sequences must retain their own order even when
            // raw anchor reordering makes their original relative interleaving impossible to preserve.
            original.Nodes.Remove(members[1]);
            original.Nodes.Add(members[1]);
        }

        var pending = original
            .Nodes
            .Where(node => !selected.Contains(node))
            .ToArray();

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(selected,
            original.Nodes.Where(node => selected.Contains(node)),
            ReferenceEqualityComparer.Instance);
        Assert.Equal(pending,
            original.Nodes.Where(node => !selected.Contains(node)),
            ReferenceEqualityComparer.Instance);
        Assert.Equal(4, original.Nodes.Count);
        Assert.Empty(transient.Nodes);
        Assert.All(selected, node => Assert.Same(original, node.Owner));
        Assert.All(selected,
            node => Assert.False(context
                .Entry(node)
                .Property(value => value.OwnerId)
                .IsModified));
        Assert.True(Members(owner, key.PrincipalToDependent!)
            .ToHashSet(ReferenceEqualityComparer.Instance)
            .SetEquals(members));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>
    ///     Explicit FK write intent restores a completed native round trip without detecting raw reorders.
    /// </summary>
    /// <param name="mode">The explicit public write, raw reorder or deliberately unmarked round trip.</param>
    [Theory]
    [InlineData("public")]
    [InlineData("raw")]
    [InlineData("public-unmarked")]
    public async Task NativeRoundTripRespectsExplicitAndUnmarkedIntent(
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
        Assert.Equal(mode == "public" ? members : pendingOrder, original.Nodes, ReferenceEqualityComparer.Instance);
        Assert.Same(original, node.Owner);
        Assert.Equal(original.Id, node.OwnerId);
        Assert.False(property.IsModified);
        Assert.Empty(transient.Nodes);
        Assert.True(Members(owner, key.PrincipalToDependent!)
            .ToHashSet(ReferenceEqualityComparer.Instance)
            .SetEquals(members));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Full FK preimages retain a generated principal's original temporary relationship sidecars.</summary>
    /// <param name="allEntries">Whether the snapshot owns all entries or only the affected hierarchy node.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporaryPrincipalRelationshipRetainsExactSidecars(
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
        entry.Reference(value => value.Owner).CurrentValue = generated;
        node.Name = "Pending name";
        entry.Property(value => value.Name).IsModified = true;
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
        entry.Property(value => value.Name).OriginalValue = "Callback original";

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
        Assert.Contains(update,
            context
                .GetService<IUpdateAdapterFactory>()
                .Create()
                .GetDependents(generatedUpdate, key));
        Assert.Same(node, Assert.Single(generated.Nodes));
        Assert.Equal(EntityState.Added, generatedEntry.State);
        Assert.Equal("Pending name", node.Name);
        Assert.Equal("Before",
            entry.Property(value => value.Name).OriginalValue);
        Assert.True(entry.Property(value => value.Name).IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Resolves the exact native entry without asking EF to detect any pending raw changes.</summary>
    private static IUpdateEntry Update(
        DbContext context,
        object entity
    )
        => context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Single(entry => ReferenceEquals(entry.ToEntityEntry().Entity,
                entity));

    /// <summary>Copies the installed native FK vector independently of current and original scalar storage.</summary>
    private static object?[] Installed(
        IUpdateEntry entry,
        IForeignKey key
    )
        => key
            .Properties
            .Select(entry.GetRelationshipSnapshotValue)
            .ToArray();

    /// <summary>Copies only actual native inverse membership, preserving absent storage as an empty set.</summary>
    private static object[] Members(
        IUpdateEntry entry,
        INavigation navigation
    )
        => (entry.GetRelationshipSnapshotValue(navigation) as IEnumerable<object> ?? []).ToArray();
}
