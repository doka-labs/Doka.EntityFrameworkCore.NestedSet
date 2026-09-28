namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetMutationFacadeTests
{
    /// <summary>Forest and subtree imports derive every structural value from their explicit destination.</summary>
    [Fact]
    public async Task BulkImportsAssignExactTreeIdentity()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var firstRoot = new TreeNode
        {
            NodeId = 1,
            Tree = 99,
            TreeId = Guid.Empty
        };

        var firstChild = new TreeNode
        {
            NodeId = 2,
            Tree = 99,
            TreeId = Guid.Empty
        };

        var secondRoot = new TreeNode
        {
            NodeId = 10,
            Tree = 99,
            TreeId = Guid.Empty
        };

        var trees = new[]
        {
            new NestedSetTreeImport<TreeNode, Guid>(
                s_firstTree,
                new NestedSetBranch<TreeNode>(firstRoot, [new NestedSetBranch<TreeNode>(firstChild)])),
            new NestedSetTreeImport<TreeNode, Guid>(s_secondTree, new NestedSetBranch<TreeNode>(secondRoot)),
        };

        // Act
        await hierarchy.InsertForestAsync(trees, CancellationToken.None);
        await hierarchy.InsertSubtreeAsync(
            new NestedSetBranch<TreeNode>(
                new TreeNode
                {
                    NodeId = 3,
                    Tree = 99,
                    TreeId = Guid.Empty,
                },
                [
                    new NestedSetBranch<TreeNode>(
                        new TreeNode
                        {
                            NodeId = 4,
                            Tree = 99,
                            TreeId = Guid.Empty
                        }),
                ]),
            firstRoot.NodeId,
            CancellationToken.None);

        var first = await hierarchy
            .InTree(s_firstTree)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        var second = await hierarchy
            .InTree(s_secondTree)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 2, 3, 4], first.Select(node => node.NodeId));
        Assert.All(first, node => Assert.Equal((7, s_firstTree), (node.Tree, node.TreeId)));
        Assert.Equal((1L, 8L, 0, 0L), (first[0].Start, first[0].End, first[0].Depth, first[0].Position));
        Assert.Equal([10], second.Select(node => node.NodeId));
        Assert.All(second, node => Assert.Equal((7, s_secondTree), (node.Tree, node.TreeId)));
    }

    /// <summary>Database-equivalent TreeIds are rejected before the first hierarchy row is inserted.</summary>
    [Fact]
    public async Task ForestRejectsDuplicateTreeIdentityAtomically()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);
        var first = new TreeNode
        {
            NodeId = 1,
            Tree = 91,
            Start = 11,
            End = 12,
        };

        var second = new TreeNode
        {
            NodeId = 2,
            Tree = 92,
            Start = 21,
            End = 22,
        };

        var imports = new[]
        {
            new NestedSetTreeImport<TreeNode, Guid>(s_firstTree, new NestedSetBranch<TreeNode>(first)),
            new NestedSetTreeImport<TreeNode, Guid>(s_firstTree, new NestedSetBranch<TreeNode>(second)),
        };

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.InsertForestAsync(imports, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, error.Code);
        Assert.Empty(
            await context
                .Set<TreeNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal((91, 11L, 12L), (first.Tree, first.Start, first.End));
        Assert.Equal((92, 21L, 22L), (second.Tree, second.Start, second.End));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Planning is read-only and rebuild repairs only the selected tree's derived structure.</summary>
    [Fact]
    public async Task TreeMaintenancePlansAndRepairsExactTree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);
        await context
            .Set<TreeNode>()
            .Where(node => node.NodeId == 2)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(node => node.Start, 100)
                    .SetProperty(node => node.End, 101),
                CancellationToken.None);

        var tree = hierarchy.InTree(s_firstTree);
        var beforePlan = await tree
            .Nodes
            .Select(node => new
            {
                node.NodeId,
                node.Start,
                node.End,
            })
            .ToArrayAsync(CancellationToken.None);

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        var plan = await tree.PlanRebuildAsync(CancellationToken.None);
        var afterPlan = await tree
            .Nodes
            .Select(node => new
            {
                node.NodeId,
                node.Start,
                node.End,
            })
            .ToArrayAsync(CancellationToken.None);

        await tree.RebuildAsync(CancellationToken.None);
        var repaired = await tree.ValidateAsync(NestedSetValidationLevel.Quick, CancellationToken.None);
        var other = await hierarchy
            .InTree(s_secondTree)
            .Nodes
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.False(report.IsValid);
        Assert.True(plan.CanRebuild);
        Assert.Equal(2, plan.NodeCount);
        Assert.True(plan.ChangedNodeCount > 0);
        Assert.True(plan.BatchCount > 0);
        Assert.Equal(beforePlan, afterPlan);
        Assert.True(repaired.IsValid);
        Assert.Equal((1L, 2L), (other.Start, other.End));
    }

    /// <summary>A corrupted TreeId with multiple roots cannot produce an ambiguous rebuild plan.</summary>
    [Fact]
    public async Task TreeMaintenanceRejectsMultipleRoots()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await context
            .Set<TreeNode>()
            .Where(node => node.NodeId == 2)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(node => node.Parent, (int?)null)
                    .SetProperty(node => node.Depth, 0)
                    .SetProperty(node => node.Position, 1),
                CancellationToken.None);

        var tree = hierarchy.InTree(s_firstTree);

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        var plan = await tree.PlanRebuildAsync(CancellationToken.None);
        var error = await Assert.ThrowsAsync<NestedSetException>(() => tree.RebuildAsync(CancellationToken.None));

        // Assert
        Assert.Contains(report.Issues, issue => issue.Code == NestedSetValidationCode.InvalidRootCount);
        Assert.False(plan.CanRebuild);
        Assert.Equal(NestedSetErrorCode.InvalidStructure, error.Code);
    }
}
