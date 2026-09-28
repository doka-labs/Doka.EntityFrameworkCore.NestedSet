namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    private static readonly int[] s_placementChildren = [3, 4, 2, 5];
    private static readonly int[] s_nodeTwoDescendants = [6];
    private static readonly int[] s_nodeSixAncestors = [1, 2];
    private static readonly int[] s_nodeSixParent = [2];
    private static readonly int[] s_placementRoots = [1];

    /// <summary>Hierarchy queries return only the requested relation in structural order.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ChildrenOfReturnsSiblingOrder()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        var actual = await tree
            .ChildrenOf(1)
            .Select(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_placementChildren, actual);
    }

    /// <summary>Hierarchy queries return only the requested relation in structural order.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DescendantsOfReturnsSubtreeOrder()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        var actual = await tree
            .DescendantsOf(2)
            .Select(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_nodeTwoDescendants, actual);
    }

    /// <summary>Hierarchy queries return only the requested relation in structural order.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AncestorsOfReturnsRootFirst()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        var actual = await tree
            .AncestorsOf(6)
            .Select(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_nodeSixAncestors, actual);
    }

    /// <summary>Hierarchy queries return only the requested relation in structural order.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ParentOfReturnsImmediateParent()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        var actual = await tree
            .ParentOf(6)
            .Select(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_nodeSixParent, actual);
    }

    /// <summary>Hierarchy queries return only the requested relation in structural order.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TreeBoundQueryReturnsItsOnlyRoot()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        var actual = await tree
            .InTree(Guid.Empty)
            .Nodes
            .Where(node => node.Parent == null)
            .Select(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_placementRoots, actual);
    }

    /// <summary>A missing anchor returns an empty hierarchy query.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ChildrenOfOfMissingNodeIsEmpty()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        var actual = await tree
            .ChildrenOf(-1)
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>A missing anchor returns an empty hierarchy query.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ParentOfOfMissingNodeIsEmpty()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        var actual = await tree
            .ParentOf(-1)
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>A missing anchor returns an empty hierarchy query.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AncestorsOfOfMissingNodeIsEmpty()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        // Act
        var actual = await tree
            .AncestorsOf(-1)
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>Query anchors are isolated by the configured scope.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DescendantsOfCannotUseAnchorFromAnotherScope()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1);

        var isolated = context
            .NestedSet<TreeNode>()
            .ForScope(2);

        await isolated.InsertRootAsync(Node(99), Guid.Empty, CancellationToken.None);

        // Act
        var actual = await tree
            .DescendantsOf(99)
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
    }
}
