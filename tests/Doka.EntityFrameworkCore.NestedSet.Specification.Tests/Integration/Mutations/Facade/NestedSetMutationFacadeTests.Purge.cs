namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetMutationFacadeTests
{
    /// <summary>A completed administrative purge removes the selected scoped tombstone.</summary>
    [Fact]
    public async Task PurgeRemovesTombstonedTreeIdentity()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.DeleteTreeAsync(s_firstTree, CancellationToken.None);

        // Act
        await hierarchy.PurgeTreeIdAsync(s_firstTree, CancellationToken.None);

        // Assert
        var mapping = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(TreeNode))!);

        var remaining = await context
            .Set<NestedSetTreeRegistry>(mapping.Registry.Name)
            .CountAsync(
                row => EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope) == 7
                    && EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == s_firstTree,
                CancellationToken.None);

        Assert.Equal(0, remaining);
    }

    /// <summary>A deliberately purged TreeId can be used for a later logical tree generation.</summary>
    [Fact]
    public async Task PurgedTreeIdCanBeReused()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.DeleteTreeAsync(s_firstTree, CancellationToken.None);
        await hierarchy.PurgeTreeIdAsync(s_firstTree, CancellationToken.None);
        var replacement = new TreeNode { NodeId = 2 };

        // Act
        await hierarchy.InsertRootAsync(replacement, s_firstTree, CancellationToken.None);

        // Assert
        var persisted = await hierarchy
            .InTree(s_firstTree)
            .Nodes
            .SingleAsync(CancellationToken.None);

        Assert.Equal((2, s_firstTree, 1L, 2L), (persisted.NodeId, persisted.TreeId, persisted.Start, persisted.End));
    }

    /// <summary>An active identity cannot be purged as if its tree had been retired.</summary>
    [Fact]
    public async Task ActiveTreeIdCannotBePurged()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.PurgeTreeIdAsync(s_firstTree, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdNotTombstoned, error.Code);
        Assert.Equal(
            1,
            await hierarchy
                .InTree(s_firstTree)
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>An unknown identity cannot be treated as proof of a previously deleted tree.</summary>
    [Fact]
    public async Task UnknownTreeIdCannotBePurged()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.PurgeTreeIdAsync(s_firstTree, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeNotFound, error.Code);
    }

    /// <summary>A damaged tombstone retaining hierarchy rows is preserved for operator investigation.</summary>
    [Fact]
    public async Task TombstoneWithNodesCannotBePurged()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        var entityType = context.Model.FindEntityType(typeof(TreeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, int>(
            entityType,
            7,
            s_firstTree,
            NestedSetTreeLockMode.Existing);

        // WHY: This out-of-protocol write deliberately models a damaged database left with live rows under a tombstone.
        await NestedSetTreeRegistryState.TombstoneAsync(context, request, CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.PurgeTreeIdAsync(s_firstTree, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, error.Code);
        Assert.Equal(1, await context.Set<TreeNode>().CountAsync(CancellationToken.None));
    }
}
