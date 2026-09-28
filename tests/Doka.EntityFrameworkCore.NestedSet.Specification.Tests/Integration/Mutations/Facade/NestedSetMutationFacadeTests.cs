namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies exact-tree facade mutations on every supported relational provider.</summary>
public abstract partial class NestedSetMutationFacadeTests : ProviderTest
{
    private static readonly Guid s_firstTree = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_secondTree = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_thirdTree = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly RelationalFixture _fixture;

    /// <summary>Creates tests backed by the reusable provider fixture.</summary>
    protected NestedSetMutationFacadeTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Creates one exact tree and derives child Scope and TreeId from its parent.</summary>
    [Fact]
    public async Task RootAndChildUseExactTreeIdentity()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        var nodes = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.Start)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            nodes,
            root => Assert.Equal(
                (1, 7, s_firstTree, null, 1L, 4L, 0, 0L),
                (root.NodeId, root.Tree, root.TreeId, root.Parent, root.Start, root.End, root.Depth, root.Position)),
            child => Assert.Equal(
                (2, 7, s_firstTree, (int?)1, 2L, 3L, 1, 0L),
                (child.NodeId, child.Tree, child.TreeId, child.Parent, child.Start, child.End, child.Depth,
                    child.Position)));
    }

    /// <summary>Independent trees keep identical local coordinates in the same Scope.</summary>
    [Fact]
    public async Task IndependentTreesKeepLocalBounds()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 2 }, s_secondTree, CancellationToken.None);
        var roots = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.All(roots, root => Assert.Equal((1L, 2L, 0, 0L), (root.Start, root.End, root.Depth, root.Position)));
    }

    /// <summary>Internal structure resolution sees filtered anchors but public queries retain the filter.</summary>
    [Fact]
    public async Task MutationIgnoresFilterAndPublicQueryPreservesIt()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context.NestedSet<UnscopedQueryNode>();
        var root = new UnscopedQueryNode
        {
            Id = 1,
            Visible = false,
        };

        var child = new UnscopedQueryNode
        {
            Id = 2,
            Visible = true,
        };

        // Act
        await hierarchy.InsertRootAsync(root, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(child, root.Id, CancellationToken.None);
        var visible = await hierarchy
            .InTree(s_firstTree)
            .Nodes
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([2], visible);
        Assert.Equal(s_firstTree, child.TreeId);
        Assert.Equal(1, child.ParentId);
    }

    /// <summary>An active TreeId cannot receive a second root.</summary>
    [Fact]
    public async Task DuplicateRootIsRejectedBeforeNodeWrite()
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
            hierarchy.InsertRootAsync(new TreeNode { NodeId = 2 }, s_firstTree, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, error.Code);
        Assert.Equal(
            1,
            await context
                .Set<TreeNode>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>A subtree can move between active trees without changing unrelated local coordinates.</summary>
    [Fact]
    public async Task MoveToTransfersSubtreeAcrossTrees()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 3 }, 2, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);

        // Act
        await hierarchy.MoveToAsync(2, 10, CancellationToken.None);
        var nodes = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            (s_firstTree, null, 1L, 2L, 0),
            (nodes[0].TreeId, nodes[0].Parent, nodes[0].Start, nodes[0].End, nodes[0].Depth));
        Assert.Equal(
            (s_secondTree, (int?)10, 2L, 5L, 1),
            (nodes[1].TreeId, nodes[1].Parent, nodes[1].Start, nodes[1].End, nodes[1].Depth));
        Assert.Equal(
            (s_secondTree, (int?)2, 3L, 4L, 2),
            (nodes[2].TreeId, nodes[2].Parent, nodes[2].Start, nodes[2].End, nodes[2].Depth));
        Assert.Equal(
            (s_secondTree, null, 1L, 6L, 0),
            (nodes[3].TreeId, nodes[3].Parent, nodes[3].Start, nodes[3].End, nodes[3].Depth));
    }

    /// <summary>Detaching a subtree creates a root and reserves its new TreeId.</summary>
    [Fact]
    public async Task DetachCreatesIndependentTree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 3 }, 2, CancellationToken.None);

        // Act
        await hierarchy.DetachAsTreeAsync(2, s_thirdTree, CancellationToken.None);
        var detached = await hierarchy
            .InTree(s_thirdTree)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            detached,
            root => Assert.Equal(
                (2, s_thirdTree, null, 1L, 4L, 0, 0L),
                (root.NodeId, root.TreeId, root.Parent, root.Start, root.End, root.Depth, root.Position)),
            child => Assert.Equal(
                (3, s_thirdTree, (int?)2, 2L, 3L, 1, 0L),
                (child.NodeId, child.TreeId, child.Parent, child.Start, child.End, child.Depth, child.Position)));
    }

    /// <summary>Deleting a root subtree tombstones the TreeId against future reuse.</summary>
    [Fact]
    public async Task DeletedTreeIdCannotBeReused()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.DeleteSubtreeAsync(1, CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.InsertRootAsync(new TreeNode { NodeId = 2 }, s_firstTree, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, error.Code);
        Assert.Empty(await context.Set<TreeNode>().ToArrayAsync(CancellationToken.None));
    }
}
