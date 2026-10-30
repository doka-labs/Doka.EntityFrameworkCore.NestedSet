namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionCapacityTests
{
    /// <summary>Both fixture topology changes reach the second primary-key window without losing its tail.</summary>
    /// <param name="repair">Whether to restore wide adjacency with the original intentional repair corruption.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task TopologyChangesCoverSecondBatchAndPreserveRootIdentity(
        bool repair
    )
    {
        // Arrange
        const int nodes = 100_001;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, nodes);

        if (repair)
        {
            await CompositionCapacityProfiles.BalanceAsync(setup, nodes);
        }

        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext(probe);
        (int NodeId, int? Parent, long Start, long End, int Depth, long Position)[] expected = repair
            ? [(1, null, 1L, 2L, 9, 0L), (100_000, 1, 1L, 2L, 9, 99_998L),
                (100_001, 1, 1L, 2L, 9, 99_999L)]
            : [(1, null, 1L, 200_002L, 0, 0L), (100_000, 99_002, 199_997L, 199_998L, 2, 997L),
                (100_001, 99_002, 199_999L, 200_000L, 2, 998L)];

        // Act
        var changed = repair
            ? await CompositionCapacityProfiles.CorruptWideForRepairAsync(context, nodes)
            : await CompositionCapacityProfiles.BalanceAsync(context, nodes);

        var rows = await context.Set<TreeNode>()
            .Where(node => node.NodeId == 1 || node.NodeId >= 100_000)
            .OrderBy(node => node.NodeId)
            .Select(node => new { node.NodeId, node.Parent, node.Start, node.End, node.Depth, node.Position })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(repair ? nodes : nodes - 1, changed);
        Assert.Equal(2, probe.NodeUpdates);
        Assert.Equal(2, probe.CompletedNodeUpdates);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(expected,
            rows.Select(node => (node.NodeId, node.Parent, node.Start, node.End, node.Depth, node.Position)));
    }
}
