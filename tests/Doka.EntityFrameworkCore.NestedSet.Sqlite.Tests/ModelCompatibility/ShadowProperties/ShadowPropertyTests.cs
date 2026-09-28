namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies mutation and query paths over shadow structural properties.</summary>
public sealed class ShadowPropertyTests : ProviderTest, IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    /// <summary>Uses immutable SQLite ownership while each test owns its local resources.</summary>
    /// <param name="fixture">The provider fixture identifying this suite's SQLite engine.</param>
    public ShadowPropertyTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>Assigns detached shadow values and persists a valid root and child.</summary>
    [Fact]
    public async Task ShadowStructureSupportsInsertAndQueryAsync()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ShadowPropertyContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        await using var context = new ShadowPropertyContext(options);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var hierarchy = context.NestedSet<ShadowNode>();
        var treeId = Guid.NewGuid();

        // Act
        await hierarchy.InsertRootAsync(
            new ShadowNode
            {
                Id = 1,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ShadowNode
            {
                Id = 2,
                Name = "Child"
            },
            1,
            CancellationToken.None);

        var rows = await hierarchy
            .InTree(treeId)
            .Nodes
            .Select(node => new
            {
                node.Id,
                ParentId = EF.Property<int?>(node, ShadowPropertyContext.ParentId),
                Left = EF.Property<long>(node, ShadowPropertyContext.Left),
                Right = EF.Property<long>(node, ShadowPropertyContext.Right),
                Depth = EF.Property<int>(node, ShadowPropertyContext.Depth),
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            rows,
            node => Assert.Equal(
                (1, (int?)null, 1L, 4L, 0),
                (node.Id, node.ParentId, node.Left, node.Right, node.Depth)),
            node => Assert.Equal((2, (int?)1, 2L, 3L, 1), (node.Id, node.ParentId, node.Left, node.Right, node.Depth)));
    }
}
