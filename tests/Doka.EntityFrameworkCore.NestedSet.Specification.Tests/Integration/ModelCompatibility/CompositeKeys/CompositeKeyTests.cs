namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies hierarchy identity that is independent of a composite EF primary key.</summary>
[Collection("Model compatibility")]
public abstract class CompositeKeyTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares one provider database with the other mapping-compatibility tests.</summary>
    protected CompositeKeyTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Uses the alternate scalar NodeKey for parent lookup and the complete composite key for persistence.</summary>
    [Fact]
    public async Task AlternateNodeKeySupportsCompositePrimaryKeyAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<CompositeKeyContext>(
            Engine,
            static options => new CompositeKeyContext(options));

        var hierarchy = context
            .NestedSet<CompositeKeyNode>()
            .ForScope(42);

        var treeId = Guid.NewGuid();
        var root = new CompositeKeyNode
        {
            RowId = 1,
            NodeKey = 100,
            Name = "Root",
        };

        var child = new CompositeKeyNode
        {
            RowId = 2,
            NodeKey = 200,
            Name = "Child",
        };

        // Act
        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(child, 100, CancellationToken.None);
        var persisted = await context
            .Set<CompositeKeyNode>()
            .AsNoTracking()
            .Where(node => node.TenantId == 42)
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            persisted,
            node =>
            {
                Assert.Equal((42, 1, 100), (node.TenantId, node.RowId, node.NodeKey));
                Assert.Null(node.ParentNodeKey);
                Assert.Equal((1L, 4L, 0), (node.Left, node.Right, node.Depth));
            },
            node =>
            {
                Assert.Equal((42, 2, 200), (node.TenantId, node.RowId, node.NodeKey));
                Assert.Equal(100, node.ParentNodeKey);
                Assert.Equal((2L, 3L, 1), (node.Left, node.Right, node.Depth));
            });
    }

    /// <summary>Automatic ordering refresh keeps equal node keys isolated by scope.</summary>
    [Fact]
    public async Task OrderedSaveRefreshesOnlyMatchingScopeAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<CompositeKeyContext>(
            Engine,
            static options => new CompositeKeyContext(options));

        var first = context
            .NestedSet<CompositeKeyNode>()
            .ForScope(1);

        var second = context
            .NestedSet<CompositeKeyNode>()
            .ForScope(2);

        await first.InsertRootAsync(
            new CompositeKeyNode
            {
                RowId = 1,
                NodeKey = 100,
                Name = "Root",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        await first.InsertChildAsync(
            new CompositeKeyNode
            {
                RowId = 2,
                NodeKey = 200,
                Name = "A",
            },
            100,
            CancellationToken.None);

        await first.InsertChildAsync(
            new CompositeKeyNode
            {
                RowId = 3,
                NodeKey = 300,
                Name = "B",
            },
            100,
            CancellationToken.None);

        await second.InsertRootAsync(
            new CompositeKeyNode
            {
                RowId = 1,
                NodeKey = 100,
                Name = "Other",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        await second.InsertChildAsync(
            new CompositeKeyNode
            {
                RowId = 2,
                NodeKey = 200,
                Name = "Foreign",
            },
            100,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var tracked = await context
            .Set<CompositeKeyNode>()
            .Where(node => node.TenantId == 1 || node.TenantId == 2)
            .OrderBy(node => node.TenantId)
            .ThenBy(node => node.RowId)
            .ToArrayAsync(CancellationToken.None);

        var changed = tracked.Single(node => node is { TenantId: 1, NodeKey: 200 });
        var foreign = tracked.Single(node => node is { TenantId: 2, NodeKey: 200 });
        changed.Name = "Z";

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal((4L, 5L, 1L), (changed.Left, changed.Right, changed.Position));
        Assert.Equal((2L, 3L, 0L), (foreign.Left, foreign.Right, foreign.Position));
        Assert.Equal("Foreign", foreign.Name);
        Assert.Equal(EntityState.Unchanged, context.Entry(foreign).State);
    }

    /// <summary>Tracked move refresh cannot adopt coordinates from another scope's equal node keys.</summary>
    [Fact]
    public async Task ParentSaveRefreshesOnlyMatchingScopeAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<CompositeKeyContext>(
            Engine,
            static options => new CompositeKeyContext(options));

        var first = context
            .NestedSet<CompositeKeyNode>()
            .ForScope(3);

        var second = context
            .NestedSet<CompositeKeyNode>()
            .ForScope(4);

        await first.InsertRootAsync(
            new CompositeKeyNode
            {
                RowId = 1,
                NodeKey = 100,
                Name = "Root",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        await first.InsertChildAsync(
            new CompositeKeyNode
            {
                RowId = 2,
                NodeKey = 200,
                Name = "Child",
            },
            100,
            CancellationToken.None);

        await first.InsertChildAsync(
            new CompositeKeyNode
            {
                RowId = 3,
                NodeKey = 300,
                Name = "Target",
            },
            100,
            CancellationToken.None);

        await second.InsertRootAsync(
            new CompositeKeyNode
            {
                RowId = 1,
                NodeKey = 100,
                Name = "Other",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        await second.InsertChildAsync(
            new CompositeKeyNode
            {
                RowId = 2,
                NodeKey = 200,
                Name = "Foreign",
            },
            100,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var tracked = await context
            .Set<CompositeKeyNode>()
            .Where(node => node.TenantId == 3 || node.TenantId == 4)
            .OrderBy(node => node.TenantId)
            .ThenBy(node => node.RowId)
            .ToArrayAsync(CancellationToken.None);

        var moved = tracked.Single(node => node is { TenantId: 3, NodeKey: 200 });
        var foreign = tracked.Single(node => node is { TenantId: 4, NodeKey: 200 });
        moved.ParentNodeKey = 300;

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal((3L, 4L, 2, 300), (moved.Left, moved.Right, moved.Depth, moved.ParentNodeKey));
        Assert.Equal((2L, 3L, 1, 100), (foreign.Left, foreign.Right, foreign.Depth, foreign.ParentNodeKey));
        Assert.Equal(EntityState.Unchanged, context.Entry(foreign).State);
    }
}
