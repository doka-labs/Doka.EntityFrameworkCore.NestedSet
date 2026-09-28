namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ContractTests
{
    /// <summary>Verifies that a generated root key is returned on the entity without leaving it tracked.</summary>
    /// <returns>A task that completes when the generated root has been verified.</returns>
    [Fact]
    public async Task DatabaseGeneratedRootKeyIsReturnedAndDetached()
    {
        // Arrange
        await using var context = await _fixture.CreateGeneratedContextAsync(Engine);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);
        var root = new TreeNode();

        // Act
        await tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
        var stored = await tree
            .InTree(Guid.Empty)
            .Nodes
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.True(root.NodeId > 0);
        Assert.Equal(root.NodeId, stored.NodeId);
        Assert.Equal((0, 0), (root.Depth, root.Position));
        Assert.Equal((1, 2, 0, 0), (stored.Start, stored.End, stored.Depth, stored.Position));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Verifies that generated child keys retain the correct parent and leave no tracked entity.</summary>
    /// <returns>A task that completes when the generated child has been verified.</returns>
    [Fact]
    public async Task DatabaseGeneratedChildKeyIsReturnedAndDetached()
    {
        // Arrange
        await using var context = await _fixture.CreateGeneratedContextAsync(Engine);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);
        var root = new TreeNode();
        await tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
        var child = new TreeNode();

        // Act
        await tree.InsertChildAsync(child, root.NodeId, cancellationToken: CancellationToken.None);
        var stored = await tree
            .InTree(Guid.Empty)
            .Nodes
            .SingleAsync(x => x.NodeId == child.NodeId, CancellationToken.None);

        // Assert
        Assert.True(child.NodeId > root.NodeId);
        Assert.Equal(root.NodeId, stored.Parent);
        Assert.Equal((1, 0), (child.Depth, child.Position));
        Assert.Equal((2, 3, 1, 0), (stored.Start, stored.End, stored.Depth, stored.Position));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Verifies that generated parent and child identities form a valid persisted hierarchy.</summary>
    /// <returns>A task that completes when the generated-key hierarchy has been validated.</returns>
    [Fact]
    public async Task DatabaseGeneratedKeysSupportValidation()
    {
        // Arrange
        await using var context = await _fixture.CreateGeneratedContextAsync(Engine);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var root = new TreeNode();
        await tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new TreeNode(), root.NodeId, cancellationToken: CancellationToken.None);

        // Act
        var errors = await tree
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Empty(errors.Issues);
    }
}
