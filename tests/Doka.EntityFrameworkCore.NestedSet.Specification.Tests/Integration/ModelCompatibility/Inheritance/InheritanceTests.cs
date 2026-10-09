namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies supported relational inheritance strategies through real hierarchy mutations.</summary>
[Collection("Model compatibility")]
public abstract partial class InheritanceTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares one provider database with the other mapping-compatibility tests.</summary>
    protected InheritanceTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Stores multiple derived node types in one complete polymorphic TPH tree.</summary>
    [Fact]
    public async Task TphSupportsPolymorphicTreeAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TphContext>(
            Engine,
            static options => new TphContext(options));

        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();

        // Act
        await hierarchy.InsertRootAsync(
            new TphFolderNode
            {
                Id = 1,
                Name = "Root",
                FolderKind = "System",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new TphMetricNode
            {
                Id = 2,
                Name = "Availability",
                Target = 99.9m,
            },
            1,
            CancellationToken.None);

        var nodes = await hierarchy
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.IsType<TphFolderNode>(nodes[0]);
        Assert.IsType<TphMetricNode>(nodes[1]);
        Assert.Equal((1L, 4L), (nodes[0].Left, nodes[0].Right));
        Assert.Equal((2L, 3L), (nodes[1].Left, nodes[1].Right));
    }

    /// <summary>Writes structural state only through the TPT base fragment while retaining derived payload.</summary>
    [Fact]
    public async Task TptSupportsStructureCoLocatedInBaseTableAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptContext>(
            Engine,
            static options => new TptContext(options));

        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();

        // Act
        await hierarchy.InsertRootAsync(
            new TptFolderNode
            {
                Id = 1,
                Name = "Root",
                FolderKind = "System",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new TptFolderNode
            {
                Id = 2,
                Name = "Child",
                FolderKind = "User",
            },
            1,
            CancellationToken.None);

        var nodes = await hierarchy
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.All(nodes, node => Assert.IsType<TptFolderNode>(node));
        Assert.Collection(
            nodes,
            node =>
            {
                Assert.Equal("Root", node.Name);
                Assert.Equal(0, node.Depth);
            },
            node =>
            {
                Assert.Equal("Child", node.Name);
                Assert.Equal(1, node.Depth);
            });
    }

    /// <summary>Coordinated saves recognize Parent changes on concrete TPH and TPT node types.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DerivedParentChangeUsesBaseHierarchyMapping(
        bool tpt
    )
    {
        // Arrange
        await using DbContext context = tpt
            ? await _fixture.CreateContextAsync<TptContext>(Engine, static options => new TptContext(options))
            : await _fixture.CreateContextAsync<TphContext>(Engine, static options => new TphContext(options));

        var rootId = tpt ? 1_001 : 2_001;
        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(rootId, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(rootId + 1, "Branch"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(rootId + 2, "Leaf"), rootId, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<InheritanceNode>()
            .SingleAsync(node => node.Id == rootId + 2, CancellationToken.None);

        changed.ParentId = rootId + 1;

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        var nodes = await context
            .Set<InheritanceNode>()
            .AsNoTracking()
            .Where(node => node.TreeId == treeId)
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(3, nodes.Length);
        Assert.Equal(rootId, nodes[0].Id);
        Assert.Equal(rootId + 1, nodes[1].Id);
        Assert.Equal(rootId + 2, nodes[2].Id);
        Assert.Equal(rootId + 1, nodes[2].ParentId);
        Assert.Equal(2, nodes[2].Depth);
        Assert.Equal((1L, 6L), (nodes[0].Left, nodes[0].Right));
        return;

        InheritanceNode Node(
            int id,
            string name
        ) => tpt
            ? new TptFolderNode
            {
                Id = id,
                Name = name,
            }
            : new TphFolderNode
            {
                Id = id,
                Name = name,
            };
    }

    /// <summary>A native save cannot bypass the coordinator through a concrete derived node type.</summary>
    [Fact]
    public async Task DirectDerivedParentSaveIsRejectedBeforeSql()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TphContext>(
            Engine,
            static options => new TphContext(options));

        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(new TphFolderNode { Id = 3_001 }, treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TphFolderNode { Id = 3_002 }, 3_001, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<InheritanceNode>()
            .SingleAsync(node => node.Id == 3_002, CancellationToken.None);

        changed.ParentId = null;

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);

        var persisted = await context
            .Set<InheritanceNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Id == 3_002, CancellationToken.None);

        Assert.Equal(3_001, persisted.ParentId);
    }
}
