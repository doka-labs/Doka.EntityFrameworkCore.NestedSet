namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks bounded tracker refresh across many ordered scopes without accepting unrelated state.</summary>
public abstract partial class OrderingRefreshTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Uses fixture-owned databases independently of the other ordering test classes.</summary>
    /// <param name="fixture">The real-provider fixture that creates isolated ordering tables.</param>
    protected OrderingRefreshTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Refreshes native tracked keys once and preserves an unaffected scope's pending snapshot.</summary>
    [Fact]
    public async Task ManyScopesRefreshTrackedBatchesOnceAndPreserveUnrelatedState()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);

        for (var scope = 1; scope <= 17; scope++)
        {
            var tree = setup
                .NestedSet<OrderingNode>()
                .ForScope(scope);

            var root = scope * 10;
            await tree.InsertRootAsync(
                new OrderingNode
                {
                    Id = root,
                    Name = "Root",
                },
                Guid.NewGuid(),
                CancellationToken.None);

            await tree.InsertChildAsync(
                new OrderingNode
                {
                    Id = root + 1,
                    Name = "Alpha",
                },
                root,
                CancellationToken.None);

            await tree.InsertChildAsync(
                new OrderingNode
                {
                    Id = root + 2,
                    Name = "Bravo",
                },
                root,
                CancellationToken.None);

            await tree.InsertChildAsync(
                new OrderingNode
                {
                    Id = root + 3,
                    Name = "Charlie",
                },
                root,
                CancellationToken.None);
        }

        var unrelatedTree = setup
            .NestedSet<OrderingNode>()
            .ForScope(1000);

        await unrelatedTree.InsertRootAsync(
            new OrderingNode
            {
                Id = 10000,
                Name = "Root",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        await unrelatedTree.InsertChildAsync(
            new OrderingNode
            {
                Id = 10001,
                Name = "Alpha",
            },
            10000,
            CancellationToken.None);

        await unrelatedTree.InsertChildAsync(
            new OrderingNode
            {
                Id = 10002,
                Name = "Bravo",
            },
            10000,
            CancellationToken.None);

        var probe = new OrderingSaveProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var tracked = await context
            .Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var unrelated = tracked.Single(node => node.Id == 10002);
        var previousBounds = (unrelated.Left, unrelated.Right, unrelated.Depth, unrelated.Position);

        // WHY: A separate completed edit makes this unrelated scope stale. The measured save must refresh only its
        // affected scopes, rather than silently replacing unrelated tracked coordinates or accepting payload originals.
        await using (var external = await _fixture.CreateContextAsync(Engine))
        {
            var moved = await external
                .Set<OrderingNode>()
                .SingleAsync(node => node.Id == 10001, CancellationToken.None);

            moved.Name = "Zulu";
            await external.SaveChangesAsync(CancellationToken.None);
        }

        var renamed = tracked
            .Where(node => node.Scope <= 17 && node.Id % 10 == 1)
            .ToArray();

        foreach (var node in renamed)
        {
            node.Name = "Zulu";
        }

        unrelated.Payload = "pending payload";
        probe.Commands.Clear();
        probe.ResetMaterializedNodes();

        // Act
        var saved = await context.SaveChangesAsync(false, CancellationToken.None);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var persistedUnrelated = await verification
            .Set<OrderingNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Id == 10002, CancellationToken.None);

        // Assert
        var lastWrite = probe.Commands.FindLastIndex(command => command
                .TrimStart()
                .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            && command.Contains("OrderingNodes", StringComparison.OrdinalIgnoreCase));

        var refreshCommands = probe
            .Commands
            .Skip(lastWrite + 1)
            .Where(command => command
                .TrimStart()
                .StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(lastWrite >= 0);
        Assert.Equal(71, tracked.Length);
        Assert.Equal(18, saved);
        Assert.Single(refreshCommands);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.All(
            refreshCommands,
            command =>
            {
                Assert.Contains("Scope", command, StringComparison.Ordinal);
                Assert.DoesNotContain("Payload", command, StringComparison.Ordinal);
                Assert.DoesNotContain("Name", command, StringComparison.Ordinal);
            });

        Assert.All(
            renamed,
            node =>
            {
                Assert.Equal((6, 7, 1, 2), (node.Left, node.Right, node.Depth, node.Position));
                var name = context
                    .Entry(node)
                    .Property(value => value.Name);
                Assert.Equal("Alpha", name.OriginalValue);
                Assert.Equal("Zulu", name.CurrentValue);
                Assert.True(name.IsModified);
            });

        Assert.Equal(previousBounds, (unrelated.Left, unrelated.Right, unrelated.Depth, unrelated.Position));
        var payload = context
            .Entry(unrelated)
            .Property(node => node.Payload);
        Assert.Equal("original", payload.OriginalValue);
        Assert.Equal("pending payload", payload.CurrentValue);
        Assert.True(payload.IsModified);
        Assert.Equal(
            (2, 3, 1, 0),
            (persistedUnrelated.Left, persistedUnrelated.Right, persistedUnrelated.Depth, persistedUnrelated.Position));

        Assert.Equal("pending payload", persistedUnrelated.Payload);
    }
}
