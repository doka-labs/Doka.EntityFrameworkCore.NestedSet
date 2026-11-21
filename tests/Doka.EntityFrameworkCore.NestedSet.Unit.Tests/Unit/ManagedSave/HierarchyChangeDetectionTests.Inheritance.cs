using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>A base-declared scope FK remains relevant when only its concrete TPC subtype is a hierarchy.</summary>
    [Fact]
    public async Task HasHierarchyChanges_InheritedScopePrincipalCollectionChange_IsObserved()
    {
        // Arrange
        await using var context = new InheritedScopeContext();
        var owner = new InheritedScopeOwner
        {
            Id = 9,
            Payload = "Original owner",
        };

        var node = new InheritedScopeNode
        {
            Id = 1,
            Scope = 7,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
        };

        context.AttachRange(owner, node);
        var ownerEntry = context.Entry(owner);
        var nodeEntry = context.Entry(node);
        owner.Payload = "Pending principal payload";
        owner.Nodes.Add(node);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(7, node.Scope);
        Assert.Null(node.ScopeOwner);
        Assert.Equal(EntityState.Unchanged, ownerEntry.State);
        Assert.Equal(EntityState.Unchanged, nodeEntry.State);
        Assert.False(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            nodeEntry.Property(value => value.Scope)
                .IsModified);

        var foreignKey = nodeEntry
            .Metadata
            .GetForeignKeys()
            .Single(key => key.Properties.Count == 1 && key.Properties[0].Name == nameof(InheritedScopeNode.Scope));

        Assert.Equal(typeof(InheritedScopeBase), foreignKey.DeclaringEntityType.ClrType);
    }

    /// <summary>Ordinary siblings in a mixed inherited collection do not create hierarchy work.</summary>
    /// <param name="remove">Whether an existing ordinary member is removed instead of being added.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HasHierarchyChanges_OrdinaryInheritedCollectionChange_PreservesUntouchedHierarchy(
        bool remove
    )
    {
        // Arrange
        await using var context = new InheritedScopeContext();
        var owner = new InheritedScopeOwner
        {
            Id = 7,
            Payload = "Original owner",
        };

        var hierarchy = new InheritedScopeNode
        {
            Id = 1,
            Scope = owner.Id,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
        };

        var ordinary = new OrdinaryScopeNode
        {
            Id = 2,
            Scope = remove
                ? owner.Id
                : 9,
        };

        context.AttachRange(owner, hierarchy, ordinary);
        var ownerEntry = context.Entry(owner);
        var hierarchyEntry = context.Entry(hierarchy);
        var ordinaryEntry = context.Entry(ordinary);
        owner.Payload = "Pending principal payload";

        if (remove)
        {
            owner.Nodes.Remove(ordinary);
        }
        else
        {
            owner.Nodes.Add(ordinary);
        }

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Same(owner, hierarchy.ScopeOwner);
        Assert.Equal(owner.Id, hierarchy.Scope);
        Assert.Contains(hierarchy, owner.Nodes);
        Assert.Equal(EntityState.Unchanged, ownerEntry.State);
        Assert.Equal(EntityState.Unchanged, hierarchyEntry.State);
        Assert.Equal(EntityState.Unchanged, ordinaryEntry.State);
        Assert.False(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            hierarchyEntry.Property(value => value.Scope)
                .IsModified);

        Assert.False(
            ordinaryEntry.Property(value => value.Scope)
                .IsModified);
    }

    /// <summary>An ordinary inherited dependent's reference edit cannot activate a sibling hierarchy mapping.</summary>
    [Fact]
    public async Task HasHierarchyChanges_OrdinaryInheritedReferenceChange_PreservesUntouchedHierarchy()
    {
        // Arrange
        await using var context = new InheritedScopeContext();
        var owner = new InheritedScopeOwner
        {
            Id = 7,
            Payload = "Original owner",
        };

        var replacement = new InheritedScopeOwner { Id = 9 };
        var hierarchy = new InheritedScopeNode
        {
            Id = 1,
            Scope = owner.Id,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
        };

        var ordinary = new OrdinaryScopeNode
        {
            Id = 2,
            Scope = owner.Id,
        };

        context.AttachRange(owner, replacement, hierarchy, ordinary);
        var ownerEntry = context.Entry(owner);
        var hierarchyEntry = context.Entry(hierarchy);
        var ordinaryEntry = context.Entry(ordinary);
        owner.Payload = "Pending principal payload";
        ordinary.ScopeOwner = replacement;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Same(owner, hierarchy.ScopeOwner);
        Assert.Equal(owner.Id, hierarchy.Scope);
        Assert.Equal(owner.Id, ordinary.Scope);
        Assert.Same(replacement, ordinary.ScopeOwner);
        Assert.Equal(EntityState.Unchanged, ownerEntry.State);
        Assert.Equal(EntityState.Unchanged, hierarchyEntry.State);
        Assert.Equal(EntityState.Unchanged, ordinaryEntry.State);
        Assert.False(
            ownerEntry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            hierarchyEntry.Property(value => value.Scope)
                .IsModified);

        Assert.False(
            ordinaryEntry.Property(value => value.Scope)
                .IsModified);
    }

    /// <summary>A duplicate hierarchy reference does not change membership in a mixed inherited collection.</summary>
    [Fact]
    public async Task HasHierarchyChanges_MixedInheritedCollection_RetainsReferenceSetSemantics()
    {
        // Arrange
        await using var context = new InheritedScopeContext();
        var owner = new InheritedScopeOwner { Id = 7 };
        var hierarchy = new InheritedScopeNode
        {
            Id = 1,
            Scope = owner.Id,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
        };

        var ordinary = new OrdinaryScopeNode
        {
            Id = 2,
            Scope = owner.Id,
        };

        context.AttachRange(owner, hierarchy, ordinary);
        owner.Nodes.Add(hierarchy);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(2, owner.Nodes.Count(value => ReferenceEquals(value, hierarchy)));
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(hierarchy)
                .State);
    }
}
