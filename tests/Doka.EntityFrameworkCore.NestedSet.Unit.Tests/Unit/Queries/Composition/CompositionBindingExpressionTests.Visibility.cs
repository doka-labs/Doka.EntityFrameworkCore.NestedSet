namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionBindingExpressionTests
{
    /// <summary>Global visibility filters remain active while explicit EF overrides retain owned identities.</summary>
    [Fact]
    public async Task VisibilityOverrideCannotRemoveScopeAndTreeBindings()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CompositionContext>()
            .UseSqlite(
                connection,
                provider => provider.UseNestedSets());
        await using var services = Services(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await context.AddRangeAsync(
            [
                new CompositionNode { Scope = Guid.Empty, Id = 1, TreeId = Guid.Empty, Left = 1, Right = 4 },
                new CompositionNode
                {
                    Scope = Guid.Empty, Id = 2, TreeId = Guid.Empty, ParentId = 1,
                    Left = 2, Right = 3, Depth = 1, Visible = false,
                },
                new CompositionNode { Scope = OtherTree, Id = 1, TreeId = Guid.Empty, Left = 1, Right = 2 },
                new CompositionNode { Scope = Guid.Empty, Id = 3, TreeId = OtherTree, Left = 1, Right = 2 },
            ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var tree = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty);

        // Act
        var visible = await tree.Nodes.Select(node => node.Id).ToArrayAsync(CancellationToken.None);
        var overridden = await tree.Nodes.IgnoreQueryFilters().Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], visible);
        Assert.Equal([1, 2], overridden);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
