namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies compatible NodeKey and Parent conversions throughout hierarchy operations.</summary>
public sealed class StrongIdTests : ProviderTest, IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    /// <summary>Uses immutable SQLite ownership while each test owns its local resources.</summary>
    /// <param name="fixture">The provider fixture identifying this suite's SQLite engine.</param>
    public StrongIdTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>Uses strong IDs in the public API while the provider compares and indexes integers.</summary>
    [Fact]
    public async Task CompatibleStrongIdConvertersSupportHierarchyOperationsAsync()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<StrongIdContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        await using var context = new StrongIdContext(options);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var hierarchy = context.NestedSet<StrongIdNode>();
        var treeId = Guid.NewGuid();
        var rootId = new StrongNodeId(1);
        var childId = new StrongNodeId(2);

        // Act
        await hierarchy.InsertRootAsync(
            new StrongIdNode
            {
                Id = rootId,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new StrongIdNode
            {
                Id = childId,
                Name = "Child",
            },
            rootId,
            CancellationToken.None);

        var child = await hierarchy
            .ChildrenOf(rootId)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(childId, child.Id);
        Assert.Equal(rootId, child.ParentId);
        Assert.Equal((2L, 3L, 1, 0L), (child.Left, child.Right, child.Depth, child.Position));
    }
}
