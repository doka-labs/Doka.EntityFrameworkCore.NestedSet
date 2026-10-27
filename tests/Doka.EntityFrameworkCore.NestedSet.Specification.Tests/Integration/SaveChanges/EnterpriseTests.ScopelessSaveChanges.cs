namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>Coordinates a tracked Parent change when TreeId alone identifies the hierarchy.</summary>
    [Fact]
    public async Task ScopelessParentChangeUsesTreeIdentity()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context.NestedSet<UnscopedQueryNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3), 1, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<UnscopedQueryNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.ParentId = 3;

        // Act
        var saved = await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal(0, saved);
        var nodes = await context
            .Set<UnscopedQueryNode>()
            .AsNoTracking()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(1, nodes[0].Id);
        Assert.Equal(3, nodes[1].Id);
        Assert.Equal(2, nodes[2].Id);
        Assert.Equal(3, nodes.Single(node => node.Id == 2).ParentId);
        Assert.Equal(2, nodes.Single(node => node.Id == 2).Depth);
        Assert.All(nodes, node => Assert.Equal(treeId, node.TreeId));
        Assert.Equal(
            Enumerable
                .Range(1, nodes.Length * 2)
                .Select(value => (long)value),
            nodes
                .SelectMany(node => new[] { node.Left, node.Right })
                .Order());
    }

    /// <summary>Creates a visible detached node whose structure is assigned by the hierarchy facade.</summary>
    /// <param name="id">The application-assigned node identity.</param>
    /// <returns>A detached visible node.</returns>
    private static UnscopedQueryNode Node(
        int id
    ) => new() { Id = id, Visible = true };
}
