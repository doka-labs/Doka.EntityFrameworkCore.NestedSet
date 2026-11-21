namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnchangedForeignKeyRollbackTests
{
    /// <summary>A matching restored FK cannot invent native membership absent before the operation.</summary>
    /// <param name="composite">Whether ordinary FKs share a composite scope member.</param>
    /// <param name="allEntries">Whether the snapshot captures all entries or only the hierarchy candidate.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Restore_OriginallyAbsentMembership_RemainsAbsentDespiteMatchingRestoredForeignKey(
        bool composite,
        bool allEntries
    )
    {
        // Arrange
        await using var context = composite
            ? new CompositeRollbackContext()
            : new RollbackContext();

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
        entry.Property(value => value.Name)
            .IsModified = true;

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

        entry.Reference(value => value.Owner)
            .CurrentValue = transient;

        node.OwnerId = original.Id;
        node.OwnerScope = original.Scope;
        entry.Property(value => value.OwnerId)
            .IsModified = true;

        if (composite)
        {
            entry.Property(value => value.OwnerScope)
                .IsModified = true;
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
        Assert.False(
            entry.Property(value => value.OwnerId)
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

    /// <summary>Supplemental repair restores selected duplicates without discarding unrelated raw list edits.</summary>
    [Fact]
    public async Task Restore_SelectedDuplicateAnchors_PreserveUnselectedPendingAddsAndRemovals()
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
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = Update(context, node);
        var originalUpdate = Update(context, original);
        var transientUpdate = Update(context, transient);
        var key = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!.ForeignKey;
        var inverse = key.PrincipalToDependent!;
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [update]);
        entry.Reference(value => value.Owner)
            .CurrentValue = transient;

        node.OwnerId = original.Id;
        entry.Property(value => value.OwnerId)
            .IsModified = true;

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
        Assert.Equal(
            pendingUnselected,
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
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(alias)
                .State);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(first)
                .State);

        Assert.False(
            entry.Property(value => value.OwnerId)
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

    /// <summary>Conflicting raw anchor order cannot reverse selected original dependent occurrences.</summary>
    /// <param name="reverseAnchors">Whether unrelated raw members are reordered after native fixup.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_MultipleSelectedMembers_KeepOriginalOrderBesidePendingAnchors(
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
        Assert.Equal(
            selected,
            original.Nodes.Where(node => selected.Contains(node)),
            ReferenceEqualityComparer.Instance);

        Assert.Equal(
            pending,
            original.Nodes.Where(node => !selected.Contains(node)),
            ReferenceEqualityComparer.Instance);

        Assert.Equal(4, original.Nodes.Count);
        Assert.Empty(transient.Nodes);
        Assert.All(selected, node => Assert.Same(original, node.Owner));
        Assert.All(
            selected,
            node => Assert.False(
                context
                    .Entry(node)
                    .Property(value => value.OwnerId)
                    .IsModified));

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
}
