namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingRefreshScaleTests
{
    /// <summary>
    ///     Verifies the published provider's native Guid collection translation above scalar parameter limits.
    /// </summary>
    [Fact]
    public async Task GuidCollectionParameterPreservesNativeIdentityBeyondScalarLimit()
    {
        // Arrange
        var database = await _relationalFixture.ResetAsync(Engine);
        await using var seed = database.CreateContext();
        var keys = Enumerable
            .Range(1, 2200)
            .Select(id => new Guid(id, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10))
            .ToArray();

        var treeId = Guid.NewGuid();
        await seed.AddRangeAsync(
            keys.Select((key, index) => new GuidNode
            {
                Id = key,
                Tree = 1,
                TreeId = treeId,
                ParentId = index == 0 ? null : keys[0],
                Left = index == 0 ? 1 : index * 2,
                Right = index == 0 ? keys.Length * 2 : (index * 2) + 1,
                Depth = index == 0 ? 0 : 1,
                Position = index == 0 ? 0 : index - 1,
            }),
            CancellationToken.None);

        await seed.SavePrecomputedHierarchyAsync(CancellationToken.None);
        using var probe = new ScaleProbe();
        await using var context = database.CreateContext(probe);
        probe.Observe(context);
        probe.Reset();

        // Act
        var actual = await context
            .Set<GuidNode>()
            .AsNoTracking()
            .Where(node => ((IEnumerable<Guid>)EF.Parameter(keys)).Contains(node.Id))
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        _output.WriteLine(
            $"Engine={Engine}; Guid keys={keys.Length}; returned={actual.Length}; "
            + $"parameters={probe.Commands.Single().Parameters}; compilations={probe.Compilations}");

        _output.WriteLine(probe.Commands.Single().Sql);

        Assert.Equal(keys.Order(), actual.Order());
        Assert.Equal(1, probe.Commands.Single().Parameters);
    }

    /// <summary>Preserves complete Guid refresh through each provider's qualified key-matching path.</summary>
    [Fact]
    public async Task GuidOrderingRefreshKeepsExactKeysAcrossBatches()
    {
        // Arrange
        await using var setup = await _keyFixture.ResetAsync(Engine);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder<OrderingKeyContext>(
                new DbContextOptions<OrderingKeyContext>(extensions))
            .ConfigureTestWarnings()
            .UseModel(setup.Model)
            .Options;

        var seedOptions = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
            .ConfigureTestWarnings()
            .UseModel(setup.Model)
            .Options;

        await using var seed = new DbContext(seedOptions);
        var treeId = Guid.NewGuid();
        var rootId = new Guid(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var root = new OrderingKeyNode<Guid, Guid?>(rootId, "Root")
        {
            Scope = 1,
            TreeId = treeId,
            Left = 1,
            Right = 132,
        };

        var input = Enumerable
            .Range(1, 65)
            .Select(id => new OrderingKeyNode<Guid, Guid?>(
                new Guid(id, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
                $"node-{id:D8}")
            {
                Scope = 1,
                TreeId = treeId,
                ParentId = rootId,
                Left = id * 2,
                Right = (id * 2) + 1,
                Depth = 1,
                Position = id - 1,
            })
            .ToArray();

        await seed.AddRangeAsync(input.Prepend(root), CancellationToken.None);
        await seed.SavePrecomputedHierarchyAsync(CancellationToken.None);
        using var probe = new ScaleProbe();
        await using var context = new OrderingKeyContext(
            new DbContextOptionsBuilder<OrderingKeyContext>(options)
                .ConfigureTestWarnings()
                .AddInterceptors(probe)
                .Options);

        var tracked = await context
            .Set<OrderingKeyNode<Guid, Guid?>>()
            .Where(node => node.ParentId != null)
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        tracked[0].Name = "zzzz";
        probe.Reset();

        // Act
        var saved = await context.SaveChangesAsync(false, CancellationToken.None);

        // Assert
        var lastWrite = probe.Commands.FindLastIndex(command => command
            .Sql
            .TrimStart()
            .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase));

        var refresh = probe
            .Commands
            .Skip(lastWrite + 1)
            .ToArray();

        _output.WriteLine(
            $"Engine={Engine}; Guid refresh commands={refresh.Length}; "
            + $"read calls={refresh.Sum(command => command.ReadCalls)}");

        Assert.Equal(1, saved);
        Assert.Equal(input.Select(node => node.Id), tracked.Select(node => node.Id));
        Assert.Equal((130, 131, 64), (tracked[0].Left, tracked[0].Right, tracked[0].Position));
        Assert.All(
            tracked.Skip(1),
            node =>
            {
                var index = Array.IndexOf(tracked, node);
                Assert.Equal((index * 2, (index * 2) + 1, index - 1), (node.Left, node.Right, node.Position));
            });

        Assert.InRange(refresh.Length, 1, 3);
        Assert.True(
            context
                .Entry(tracked[0])
                .Property(node => node.Name)
                .IsModified);
    }
}
