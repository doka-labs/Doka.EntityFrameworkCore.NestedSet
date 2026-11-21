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
    public async Task ChildrenOf_ExistingAnchor_ReturnsSiblingOrder()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task DescendantsOf_ExistingAnchor_ReturnsSubtreeOrder()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task AncestorsOf_ExistingAnchor_ReturnsRootFirst()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task ParentOf_ExistingChild_ReturnsImmediateParent()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task InTree_TreeBoundQuery_ReturnsItsOnlyRoot()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task ChildrenOf_ChildrenOfOfMissingNode_IsEmpty()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task ParentOf_ParentOfOfMissingNode_IsEmpty()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task AncestorsOf_AncestorsOfOfMissingNode_IsEmpty()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task DescendantsOf_ForeignScopeAnchor_CannotReturnDescendants()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

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
