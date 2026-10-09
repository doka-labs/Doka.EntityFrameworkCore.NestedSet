using static Doka.EntityFrameworkCore.NestedSet.Tests.ConcurrentCapacityTestSupport;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies serialized same-tree writes with 64 real relational writers on every engine.</summary>
public abstract class ConcurrentWriterTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the provider suite's isolated database fixture.</summary>
    /// <param name="fixture">The fixture owning the selected relational engine.</param>
    protected ConcurrentWriterTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Releases 64 writers together to append children beneath the same existing tree root.</summary>
    /// <returns>A task that completes after checking serialization, dense positions, and full tree validity.</returns>
    [Fact]
    public async Task SameTreeSerializes64ChildWritersWithDensePositions()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = TreeId(1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using (var setup = database.CreateContext())
        {
            await setup
                .NestedSet<ConcurrentNode>()
                .ForScope(Scope)
                .InsertRootAsync(new ConcurrentNode { Id = 1 }, treeId, deadline.Token);
        }

        var barrier = new ConcurrentCapacityBarrier(WriterCount);
        var writerIds = Enumerable
            .Range(2, WriterCount)
            .ToArray();

        // Act
        await Task.WhenAll(writerIds.Select(id => InsertContendingChildAsync(database, id, barrier, deadline.Token)));

        // Assert
        Assert.Equal(WriterCount, barrier.Arrivals);
        await using var verification = database.CreateContext();
        var nodes = await verification
            .Set<ConcurrentNode>()
            .AsNoTracking()
            .OrderBy(node => node.Left)
            .ToArrayAsync(deadline.Token);

        Assert.Equal(WriterCount + 1, nodes.Length);
        Assert.All(nodes, node => Assert.Equal((Scope, treeId), (node.Tree, node.TreeId)));

        var root = nodes[0];
        Assert.Equal(1, root.Id);
        Assert.Equal(
            (1L, (WriterCount + 1) * 2L, 0, 0L, (int?)null),
            (root.Left, root.Right, root.Depth, root.Position, root.ParentId));
        Assert.Equal(
            Enumerable.Range(1, WriterCount + 1),
            nodes
                .Select(node => node.Id)
                .OrderBy(id => id));

        for (var index = 0; index < WriterCount; index++)
        {
            var child = nodes[index + 1];
            Assert.Equal(
                (2L + (index * 2L), 3L + (index * 2L), 1, (long)index, (int?)1),
                (child.Left, child.Right, child.Depth, child.Position, child.ParentId));
        }

        Assert.Empty(
            (await verification
                .NestedSet<ConcurrentNode>()
                .ForScope(Scope)
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, deadline.Token)).Issues);
    }

    /// <summary>Waits before transaction acquisition so SQLite can also serialize every released writer.</summary>
    private static async Task InsertContendingChildAsync(
        TestDatabase database,
        int id,
        ConcurrentCapacityBarrier barrier,
        CancellationToken cancellationToken
    )
    {
        await using var context = database.CreateContext();
        await barrier.ArriveAsync(cancellationToken);
        await context
            .NestedSet<ConcurrentNode>()
            .ForScope(Scope)
            .InsertChildAsync(new ConcurrentNode { Id = id }, 1, cancellationToken);
    }

}
