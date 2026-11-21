namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class InsertionReflectionContractTests
{
    /// <summary>Snapshot restoration changes neither current CLR relationships nor their ordinary originals.</summary>
    [Fact]
    public async Task SetRelationshipSnapshot_RelationshipSnapshotSetter_SeparatesStoredAndCurrentValues()
    {
        // Arrange
        await using var context = new RelationshipContext();
        var parent = new RelationshipEntity { Id = 1 };
        var replacement = new RelationshipEntity { Id = 3 };
        var child = new RelationshipEntity
        {
            Id = 2,
            ParentId = 1,
            Parent = parent,
        };

        parent.Children.Add(child);
        context.AttachRange(parent, replacement);
        var contract = NestedSetInsertionContract.Instance;
        var update = contract.Entry(context.Entry(child));
        var parentProperty = update.EntityType.FindProperty(nameof(RelationshipEntity.ParentId))!;
        var parentNavigation = update.EntityType.FindNavigation(nameof(RelationshipEntity.Parent))!;
        child.ParentId = 3;
        child.Parent = replacement;

        // Act
        contract.SetRelationshipSnapshot(update, parentProperty, 7);
        contract.SetRelationshipSnapshot(update, parentNavigation, replacement);

        // Assert
        Assert.Equal(7, update.GetRelationshipSnapshotValue(parentProperty));
        Assert.Same(replacement, update.GetRelationshipSnapshotValue(parentNavigation));
        Assert.Equal(3, child.ParentId);
        Assert.Same(replacement, child.Parent);
        Assert.Equal(1, update.GetOriginalValue<int?>(parentProperty));
        Assert.False(update.IsModified(parentProperty));
        Assert.Equal(EntityState.Unchanged, update.EntityState);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Known detached members enter only the native reference set, without implicit graph tracking.</summary>
    [Fact]
    public async Task AddCollectionSnapshot_CollectionSnapshotAddition_RetainsDistinctReferencesWithoutTrackingThem()
    {
        // Arrange
        await using var context = new RelationshipContext();
        var parent = new RelationshipEntity { Id = 1 };
        context.Attach(parent);
        var first = new RelationshipEntity { Id = 2 };
        var sameKey = new RelationshipEntity { Id = 2 };
        parent.Children.Add(first);
        parent.Children.Add(sameKey);
        var contract = NestedSetInsertionContract.Instance;
        var update = contract.Entry(context.Entry(parent));
        var navigation = update.EntityType.FindNavigation(nameof(RelationshipEntity.Children))!;

        // Act
        contract.AddCollectionSnapshot(update, navigation, first);
        contract.AddCollectionSnapshot(update, navigation, sameKey);
        contract.AddCollectionSnapshot(update, navigation, first);

        // Assert
        var snapshot = Assert.IsAssignableFrom<ISet<object>>(update.GetRelationshipSnapshotValue(navigation));
        Assert.Equal(2, snapshot.Count);
        Assert.True(snapshot.Contains(first));
        Assert.True(snapshot.Contains(sameKey));
        Assert.Collection(
            parent.Children,
            member => Assert.Same(first, member),
            member => Assert.Same(sameKey, member));

        Assert.Same(
            parent,
            Assert.Single(context.ChangeTracker.Entries())
                .Entity);

        Assert.Equal(EntityState.Unchanged, update.EntityState);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Removing a recorded relationship never edits its current collection or tracked entity state.</summary>
    [Fact]
    public async Task RemoveCollectionSnapshot_CollectionSnapshotRemoval_PreservesCurrentMembers()
    {
        // Arrange
        await using var context = new RelationshipContext();
        var parent = new RelationshipEntity { Id = 1 };
        var child = new RelationshipEntity
        {
            Id = 2,
            ParentId = 1,
            Parent = parent,
        };

        parent.Children.Add(child);
        context.Attach(parent);
        var contract = NestedSetInsertionContract.Instance;
        var update = contract.Entry(context.Entry(parent));
        var navigation = update.EntityType.FindNavigation(nameof(RelationshipEntity.Children))!;

        // Act
        contract.RemoveCollectionSnapshot(update, navigation, child);

        // Assert
        Assert.Empty(Assert.IsAssignableFrom<ISet<object>>(update.GetRelationshipSnapshotValue(navigation)));
        Assert.Same(child, Assert.Single(parent.Children));
        Assert.Same(parent, child.Parent);
        Assert.Equal(1, child.ParentId);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
