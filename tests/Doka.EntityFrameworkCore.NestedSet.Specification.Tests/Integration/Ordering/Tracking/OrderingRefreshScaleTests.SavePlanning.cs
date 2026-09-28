namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingRefreshScaleTests
{
    /// <summary>Many changes in one tree contribute one lock and refresh identity during a Parent move.</summary>
    [Fact]
    public async Task ParentSaveDeduplicatesTreeRequests()
    {
        // Arrange
        // WHY: SQL Server needs more than 1,050 candidates to cross its 2,100 scalar-parameter limit.
        var children = Engine == "SqlServer" ? 1052 : 128;

        await using var seed = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        var nodes = new List<OrderingNode>(children + 1)
        {
            new()
            {
                Id = 1,
                Scope = 1,
                TreeId = treeId,
                Name = "Root",
                Left = 1,
                Right = (children + 1L) * 2,
            },
        };

        for (var index = 0; index < children; index++)
        {
            nodes.Add(
                new OrderingNode
                {
                    Id = index + 2,
                    Scope = 1,
                    TreeId = treeId,
                    Name = $"node-{index:D8}",
                    ParentId = 1,
                    Left = (index * 2L) + 2,
                    Right = (index * 2L) + 3,
                    Depth = 1,
                    Position = index,
                });
        }

        await seed.AddRangeAsync(nodes, CancellationToken.None);
        await seed.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict");
        var tracked = await context
            .Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        foreach (var child in tracked.Skip(1))
        {
            child.Name = $"renamed-{child.Id:D8}";
            child.Payload = "updated";
        }

        tracked[1].ParentId = tracked[2].Id;

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(children, saved);
        await using var verification = await _fixture.CreateContextAsync(Engine, "Strict");
        var moved = await verification
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);
        Assert.Equal(3, moved.ParentId);
        Assert.Equal(2, moved.Depth);
        Assert.Equal(
            children,
            await verification
                .Set<OrderingNode>()
                .CountAsync(node => node.Payload == "updated", CancellationToken.None));
        Assert.Equal(
            children,
            await verification
                .Set<OrderingNode>()
                .CountAsync(node => node.Name!.StartsWith("renamed-"), CancellationToken.None));
    }
}
