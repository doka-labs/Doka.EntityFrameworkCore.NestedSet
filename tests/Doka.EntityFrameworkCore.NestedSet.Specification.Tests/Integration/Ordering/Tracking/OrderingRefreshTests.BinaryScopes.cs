namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingRefreshTests
{
    /// <summary>Groups equal binary scope contents without one membership branch per array instance.</summary>
    [Fact]
    public async Task EqualBinaryScopesShareOneMembershipBatch()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var probe = new RefreshScopeProbe();
        var options = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
            .ConfigureTestWarnings()
            .AddInterceptors(probe)
            .Options;

        await using var context = new BinaryScopeContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var seedOptions = new DbContextOptionsBuilder(options)
            .ConfigureTestWarnings()
            .UseModel(context.Model)
            .Options;

        await using var seed = new DbContext(seedOptions);
        var treeId = Guid.NewGuid();
        var root = new BinaryScopeNode
        {
            Id = 0,
            Scope = [1, 2, 3, 4],
            TreeId = treeId,
            Name = "Root",
            Left = 1,
            Right = 402,
        };

        var input = Enumerable
            .Range(0, 200)
            .Select(position => new BinaryScopeNode
            {
                Id = position + 1,
                Scope = [1, 2, 3, 4],
                TreeId = treeId,
                ParentId = 0,
                Name = $"node-{position:D3}",
                Left = (position * 2) + 2,
                Right = (position * 2) + 3,
                Depth = 1,
                Position = position,
            })
            .ToArray();

        await seed.AddRangeAsync(input.Prepend(root), CancellationToken.None);
        await seed.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var tracked = await context
            .Set<BinaryScopeNode>()
            .Where(node => node.ParentId != null)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        tracked[0].Name = "node-001z";
        probe.Commands.Clear();

        // Act
        var saved = await context.SaveChangesAsync(false, CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.NotSame(tracked[0].Scope, tracked[1].Scope);
        Assert.Equal((4, 5, 1), (tracked[0].Left, tracked[0].Right, tracked[0].Position));
        Assert.Equal((2, 3, 0), (tracked[1].Left, tracked[1].Right, tracked[1].Position));
        Assert.Equal((400, 401, 199), (tracked[^1].Left, tracked[^1].Right, tracked[^1].Position));
        var lastWrite = probe.Commands.FindLastIndex(command => command
            .TrimStart()
            .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase));

        Assert.True(lastWrite >= 0);
        var refresh = probe
            .Commands
            .Skip(lastWrite + 1)
            .ToArray();

        // WHY: One scope membership query plus four bounded key batches covers 200 separately allocated scopes.
        Assert.InRange(refresh.Length, 1, 5);
        Assert.All(
            refresh,
            sql =>
            {
                Assert.DoesNotContain("Payload", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("Name", sql, StringComparison.Ordinal);
            });
    }
}
