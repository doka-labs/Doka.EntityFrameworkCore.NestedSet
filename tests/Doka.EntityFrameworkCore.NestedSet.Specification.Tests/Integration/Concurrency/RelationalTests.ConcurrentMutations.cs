namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Verifies competing mutations preserve one tree's local bounds and adjacency.</summary>
    /// <param name="operation">The mutation dispatched by both independently owned contexts.</param>
    [Theory]
    [InlineData("Move")]
    [InlineData("Delete")]
    [InlineData("Rebuild")]
    [InlineData("MoveDelete")]
    [InlineData("MoveRebuild")]
    [InlineData("DeleteRebuild")]
    public async Task ConcurrentMutationsPreserveTheForest(
        string operation
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        var tree = setup
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPlacementTreeAsync(setup, 1);
        await tree.InsertChildAsync(Node(7), 1, CancellationToken.None);

        if (operation == "Rebuild")
        {
            await setup
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 1)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(node => node.Start, 1)
                        .SetProperty(node => node.End, 2),
                    CancellationToken.None);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        var lockBarrier = new MutationLockBarrier(setup.Model.FindEntityType(typeof(TreeNode))!, 1, Guid.Empty);

        var first = MutateAsync(2);
        var second = MutateAsync(3);
        await ready.Task.WaitAsync(timeout.Token);

        // Act
        // WHY: Both contexts reach the barrier before release; correctness cannot depend on task startup order.
        release.SetResult();
        await Task.WhenAll(first, second);

        // Assert
        Assert.Equal(2, arrivals);
        Assert.Equal(Engine == "Sqlite" ? 0 : 2, lockBarrier.Arrivals);
        await AssertValidAsync(setup, 1);
        var nodes = await setup
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .OrderBy(node => node.NodeId)
            .ToArrayAsync(timeout.Token);

        var expectedCount = operation switch
        {
            "Delete" => 5,
            "MoveDelete" or "DeleteRebuild" => 6,
            _ => 7,
        };

        Assert.Equal(expectedCount, nodes.Length);

        if (operation is "Move" or "MoveDelete" or "MoveRebuild")
        {
            Assert.Equal(7, nodes.Single(node => node.NodeId == 2).Parent);
            Assert.Equal(2, nodes.Single(node => node.NodeId == 6).Parent);
        }

        if (operation is "Delete" or "DeleteRebuild")
        {
            Assert.DoesNotContain(nodes, node => node.NodeId == 2);
            Assert.Equal(1, nodes.Single(node => node.NodeId == 6).Parent);
        }

        if (operation is "Delete" or "MoveDelete")
        {
            Assert.DoesNotContain(nodes, node => node.NodeId == 3);
        }
        else
        {
            Assert.Equal(operation == "Move" ? 7 : 1, nodes.Single(node => node.NodeId == 3).Parent);
        }

        if (operation == "Rebuild")
        {
            Assert.Equal(1, nodes.Single(node => node.NodeId == 2).Parent);
            Assert.Equal(2, nodes.Single(node => node.NodeId == 6).Parent);
        }

        Assert.Null(nodes.Single(node => node.NodeId == 1).Parent);
        Assert.Equal(1, nodes.Single(node => node.NodeId == 7).Parent);
        Assert.All(nodes.Where(node => node.NodeId is 4 or 5), node => Assert.Equal(1, node.Parent));
        return;

        async Task MutateAsync(
            int key
        )
        {
            // WHY: SQLite reserves its sole writer during transaction begin, before the lock command; waiting
            // there for a second lock command would deadlock the test. Server transactions reach both commands.
            await using var context = Engine == "Sqlite"
                ? database.CreateContext()
                : database.CreateContext(lockBarrier);

            var scoped = context
                .NestedSet<TreeNode>()
                .ForScope(1);

            if (Interlocked.Increment(ref arrivals) == 2)
            {
                ready.TrySetResult();
            }

            await release.Task.WaitAsync(timeout.Token);

            var selected = operation switch
            {
                "MoveDelete" => key == 2 ? "Move" : "Delete",
                "MoveRebuild" => key == 2 ? "Move" : "Rebuild",
                "DeleteRebuild" => key == 2 ? "Delete" : "Rebuild",
                _ => operation,
            };

            switch (selected)
            {
                case "Move":
                    await scoped.MoveToAsync(key, 7, timeout.Token);
                    break;
                case "Delete":
                    await scoped.DeleteAsync(key, timeout.Token);
                    break;
                case "Rebuild":
                    await scoped
                        .InTree(Guid.Empty)
                        .RebuildAsync(timeout.Token);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }
}
