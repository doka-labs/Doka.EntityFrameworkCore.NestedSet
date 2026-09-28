namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Verifies current-table mutations and read-only history access for SQL Server temporal nodes.</summary>
public sealed class TemporalTests : ProviderTest, IClassFixture<ProviderFixture<ProviderResources, SqlServerEngine>>
{
    /// <summary>Uses immutable provider ownership while each case retains its isolated database.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    public TemporalTests(
        ProviderFixture<ProviderResources, SqlServerEngine> fixture
    ) : base(fixture) { }

    /// <summary>Mutates current hierarchy rows while SQL Server retains queryable historical versions.</summary>
    [Fact]
    public async Task CurrentTableMutationPreservesTemporalQuerySupportAsync()
    {
        // Arrange
        var connection = await (await TestDatabaseServers.GetCurrentAsync()).NewConnectionStringAsync(Engine);
        var options = new DbContextOptionsBuilder<TemporalContext>()
            .ConfigureTestWarnings()
            .UseSqlServer(connection)
            .UseNestedSets()
            .Options;

        await using var context = new TemporalContext(options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var hierarchy = context.NestedSet<TemporalNode>();
        var treeId = Guid.NewGuid();

        // Act
        await hierarchy.InsertRootAsync(
            new TemporalNode
            {
                Id = 1,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new TemporalNode
            {
                Id = 2,
                Name = "Child",
            },
            1,
            CancellationToken.None);

        var current = await hierarchy
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        var temporalCount = await context
            .Set<TemporalNode>()
            .TemporalAll()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, current.Length);
        Assert.True(temporalCount >= current.Length);
        Assert.Equal((1L, 4L), (current[0].Left, current[0].Right));
        Assert.Equal((2L, 3L), (current[1].Left, current[1].Right));
    }
}
