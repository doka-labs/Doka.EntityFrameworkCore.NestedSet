namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies native sibling rules and manual-order preservation during complete-branch imports.</summary>
public abstract class BulkOrderingTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Uses the existing independently isolated strict, flexible and converted ordering mappings.</summary>
    protected BulkOrderingTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Imported sibling groups merge with persisted siblings while retaining hierarchical preorder.</summary>
    [Fact]
    public async Task StrictForestMergesSubtreesInConfiguredOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = context.NestedSet<OrderingNode>().ForScope(1);
        await tree.InsertRootAsync(new OrderingNode { Id = 100, Name = "Root" }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new OrderingNode { Id = 10, Name = "Middle" }, 100, CancellationToken.None);
        var root = new OrderingNode { Id = 1, Name = "Alpha" };
        var zulu = new OrderingNode { Id = 2, Name = "Zulu" };
        var alpha = new OrderingNode { Id = 3, Name = "Alpha" };
        var leaf = new OrderingNode { Id = 4, Name = "Deep" };
        var branch = new NestedSetBranch<OrderingNode>(root,
        [
            new NestedSetBranch<OrderingNode>(zulu, [new NestedSetBranch<OrderingNode>(leaf)]),
            new NestedSetBranch<OrderingNode>(alpha),
        ]);

        var sibling = new NestedSetBranch<OrderingNode>(new OrderingNode { Id = 5, Name = "Zulu" });

        // Act
        await tree.InsertSubtreeAsync(branch, 100, CancellationToken.None);
        await tree.InsertSubtreeAsync(sibling, 100, CancellationToken.None);
        var actual = await tree.InTree(Guid.Empty).Nodes
            .Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        var validation = await tree.InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Equal([100, 1, 3, 2, 4, 10, 5], actual);
        Assert.Equal((2, 9, 1, 0), (root.Left, root.Right, root.Depth, root.Position));
        Assert.Equal((5, 8, 2, 1), (zulu.Left, zulu.Right, zulu.Depth, zulu.Position));
        Assert.Equal(2, leaf.ParentId);
        Assert.True(validation.IsValid);
    }

    /// <summary>Flexible import follows native predecessors without reversing existing manual placements.</summary>
    [Fact]
    public async Task FlexibleForestPreservesExistingManualRelativeOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine, "Flexible");
        var tree = context.NestedSet<OrderingNode>().ForScope(1);
        await tree.InsertRootAsync(new OrderingNode { Id = 100, Name = "Root" }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new OrderingNode { Id = 1, Name = "Alpha" }, 100, CancellationToken.None);
        await tree.InsertBeforeAsync(new OrderingNode { Id = 2, Name = "Zulu" }, 1, CancellationToken.None);
        var middle = new NestedSetBranch<OrderingNode>(new OrderingNode { Id = 3, Name = "Middle" });
        var bravo = new NestedSetBranch<OrderingNode>(new OrderingNode { Id = 4, Name = "Bravo" });

        // Act
        await tree.InsertSubtreeAsync(middle, 100, CancellationToken.None);
        await tree.InsertSubtreeAsync(bravo, 100, CancellationToken.None);
        var actual = await tree.ChildrenOf(100).Select(node => node.Id).ToArrayAsync(CancellationToken.None);
        var validation = await tree.InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Equal([2, 1, 4, 3], actual);
        Assert.True(validation.IsValid);
    }

    /// <summary>Converted sort values use storage order rather than incompatible CLR enum ordinals.</summary>
    [Fact]
    public async Task ConvertedDomainRuleUsesDatabaseRepresentation()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine, "Converted");
        var tree = context.NestedSet<OrderingNode>().ForScope(1);
        var branch = new NestedSetBranch<OrderingNode>(new OrderingNode { Id = 100 },
        [
            new NestedSetBranch<OrderingNode>(new OrderingNode { Id = 1, Category = OrderingCategory.Zeta }),
            new NestedSetBranch<OrderingNode>(new OrderingNode { Id = 2, Category = OrderingCategory.Alpha }),
            new NestedSetBranch<OrderingNode>(new OrderingNode { Id = 3, Category = OrderingCategory.Beta }),
        ]);

        // Act
        await tree.InsertForestAsync(
            [new NestedSetTreeImport<OrderingNode, Guid>(Guid.Empty, branch)], CancellationToken.None);
        var actual = await tree.ChildrenOf(100).Select(node => node.Id).ToArrayAsync(CancellationToken.None);
        var validation = await tree.InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Equal([2, 3, 1], actual);
        Assert.True(validation.IsValid);
    }
}
