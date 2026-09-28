namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetFacadeTests
{
    /// <summary>Scopeless tree queries preserve application filters and stable preorder.</summary>
    [Fact]
    public async Task ScopelessTreeRespectsGlobalQueryFilter()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedScopelessAsync(database);
        await using var context = database.CreateContext();

        // Act
        var ids = await context
            .NestedSet<UnscopedQueryNode>()
            .InTree(s_firstTree)
            .Nodes
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([100, 102, 103], ids);
    }

    /// <summary>A hidden anchor cannot expose descendants through a public hierarchy query.</summary>
    [Fact]
    public async Task FilteredAnchorReturnsEmptyQuery()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedScopelessAsync(database);
        await using var context = database.CreateContext();

        // Act
        var descendants = await context
            .NestedSet<UnscopedQueryNode>()
            .DescendantsOf(101)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(descendants);
    }

    /// <summary>Facade queries do not create tracked entries by default.</summary>
    [Fact]
    public async Task QueriesAreNoTrackingByDefault()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedScopedAsync(database);
        await using var context = database.CreateContext();

        // Act
        var nodes = await context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_firstTree)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(4, nodes.Length);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Normal EF Core AsTracking explicitly opts a facade query into tracking.</summary>
    [Fact]
    public async Task AsTrackingOptsIntoChangeTracking()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedScopedAsync(database);
        await using var context = database.CreateContext();

        // Act
        var nodes = await context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_firstTree)
            .Nodes
            .AsTracking()
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(4, nodes.Length);
        Assert.Equal(
            4,
            context
                .ChangeTracker
                .Entries<TreeNode>()
                .Count());
    }
}
