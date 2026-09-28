namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies bounded automatic reorder batches for converted NodeKeys.</summary>
[Collection("Model compatibility")]
public abstract class OrderedStrongIdTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares one provider database with the other mapping-compatibility tests.</summary>
    protected OrderedStrongIdTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Strict reorder handles more converted keys than SQL Server accepts as scalar parameters.</summary>
    [Fact]
    public async Task StrictReorderBatchesManyConvertedKeysAsync()
    {
        // Arrange
        const int children = 2200;
        await using var context = await _fixture.CreateContextAsync<StrictStrongIdContext>(
            Engine,
            static options => new StrictStrongIdContext(options));

        var treeId = await SeedAsync(context, 1, children);
        var tracked = await RenameReversedAsync(context, treeId, 1, children);

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        await AssertReversedAsync(context, treeId, tracked, 1, children);
    }

    /// <summary>Manual-placement reorder keeps rule order when one tree's changes span key batches.</summary>
    [Fact]
    public async Task PlacedReorderKeepsRuleOrderAcrossKeyBatchesAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<PlacedStrongIdContext>(
            Engine,
            static options => new PlacedStrongIdContext(options));

        // WHY: Both trees need more than one key batch in total; each placement depends on the rule
        // predecessor, so a wrong order across batches leaves a node after the wrong sibling.
        var small = await SeedAsync(context, 1, 40);
        var large = await SeedAsync(context, 1000, 150);
        var smallNodes = await RenameReversedAsync(context, small, 1, 40);
        var largeNodes = await RenameReversedAsync(context, large, 1000, 150);

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        await AssertReversedAsync(context, small, smallNodes, 1, 40);
        await AssertReversedAsync(context, large, largeNodes, 1000, 150);
    }

    /// <summary>Stores one root and its children in ascending rule order with precomputed bounds.</summary>
    private static async Task<Guid> SeedAsync(
        DbContext context,
        int root,
        int children
    )
    {
        var treeId = Guid.NewGuid();
        context.Add(
            new OrderedStrongIdNode
            {
                Id = new StrongNodeId(root),
                TreeId = treeId,
                Name = "root",
                Left = 1,
                Right = (2L * children) + 2,
            });

        for (var index = 0; index < children; index++)
        {
            context.Add(
                new OrderedStrongIdNode
                {
                    Id = new StrongNodeId(root + 1 + index),
                    TreeId = treeId,
                    ParentId = new StrongNodeId(root),
                    Name = $"n{index:D4}",
                    Left = (2L * index) + 2,
                    Right = (2L * index) + 3,
                    Depth = 1,
                    Position = index,
                });
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        return treeId;
    }

    /// <summary>Tracks every child and renames it so the configured rule reverses the sibling order.</summary>
    private static async Task<OrderedStrongIdNode[]> RenameReversedAsync(
        DbContext context,
        Guid treeId,
        int root,
        int children
    )
    {
        var tracked = await context
            .Set<OrderedStrongIdNode>()
            .Where(node => node.TreeId == treeId && node.Depth == 1)
            .ToArrayAsync(CancellationToken.None);

        foreach (var node in tracked)
        {
            node.Name = $"r{children + root - node.Id.Value:D4}";
        }

        return tracked;
    }

    /// <summary>Checks persisted order, refreshed tracked coordinates, and full tree validity.</summary>
    private static async Task AssertReversedAsync(
        DbContext context,
        Guid treeId,
        OrderedStrongIdNode[] tracked,
        int root,
        int children
    )
    {
        var ordered = await context
            .Set<OrderedStrongIdNode>()
            .AsNoTracking()
            .Where(node => node.TreeId == treeId && node.Depth == 1)
            .OrderBy(node => node.Left)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(
            Enumerable
                .Range(root + 1, children)
                .Reverse()
                .Select(value => new StrongNodeId(value)),
            ordered);

        Assert.All(
            tracked,
            node =>
            {
                var position = (long)children + root - node.Id.Value;
                Assert.Equal(((2 * position) + 2, (2 * position) + 3, position), (node.Left, node.Right, node.Position));
            });

        var report = await context
            .NestedSet<OrderedStrongIdNode>()
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        Assert.True(report.IsValid);
    }
}
