namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>Verifies that binary-key CASE batches compare independently allocated keys by value.</summary>
    /// <returns>A task that completes after checking every repaired coordinate across three batches.</returns>
    [Fact]
    public async Task BinaryKeysRemainCorrectAcrossRepairBatches()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();

        // WHY: Distinct positive leaf intervals satisfy database checks while remaining inconsistent with the
        // star adjacency, so Rebuild must still repair every row across three bounded batches.
        await setup.AddRangeAsync(
            Enumerable
                .Range(1, 129)
                .Select(id => new BinaryNode
                {
                    Id = BitConverter.GetBytes(id),
                    ParentId = id == 1 ? null : BitConverter.GetBytes(1),
                    Tree = 1,
                    Left = (id * 2L) - 1,
                    Right = id * 2L,
                    Depth = 0,
                    Position = id == 1 ? 0 : id - 2,
                }),
            CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var probe = new EnterpriseProbe("BinaryNode");
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        await tree.RebuildAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, probe.NodeUpdates);
        Assert.Equal(0, probe.MaterializedNodes);
        await using var verification = database.CreateContext();
        var nodes = await verification
            .Set<BinaryNode>()
            .OrderBy(node => node.Left)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(129, nodes.Count);
        Assert.Equal(Enumerable.Range(1, 129), nodes.Select(node => BitConverter.ToInt32(node.Id)));
        Assert.Equal((1L, 258L, 0, 0L), (nodes[0].Left, nodes[0].Right, nodes[0].Depth, nodes[0].Position));
        Assert.All(
            nodes.Skip(1),
            node =>
            {
                var id = BitConverter.ToInt32(node.Id);
                Assert.Equal(
                    (((long)id * 2) - 2, ((long)id * 2) - 1, 1, (long)id - 2),
                    (node.Left, node.Right, node.Depth, node.Position));
                Assert.Equal(BitConverter.GetBytes(1), node.ParentId);
            });
    }

    /// <summary>
    ///     Verifies string-key repair batches retain stored parent aliases while using database equality.
    /// </summary>
    /// <returns>A task that completes after checking three batches and all stored parent keys.</returns>
    [Fact]
    public async Task StringKeysAndParentAliasesRemainCorrectAcrossRepairBatches()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var parent = Engine == "PostgreSql" ? "ROOT" : "root";
        await using var setup = database.CreateContext();

        // WHY: Constraint-valid but adjacency-inconsistent leaves keep the repair path reachable on every provider.
        await setup.AddRangeAsync(
            Enumerable
                .Range(1, 129)
                .Select(id => new TextNode
                {
                    Id = id == 1 ? "ROOT" : $"node{id:D4}",
                    ParentId = id == 1 ? null : parent,
                    Tree = "scope",
                    Left = (id * 2L) - 1,
                    Right = id * 2L,
                    Depth = 0,
                    Position = id == 1 ? 0 : id - 2,
                }),
            CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var probe = new EnterpriseProbe("TextNode");
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TextNode>()
            .ForScope("scope")
            .InTree(Guid.Empty);

        // Act
        await tree.RebuildAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, probe.NodeUpdates);
        Assert.Equal(0, probe.MaterializedNodes);
        await using var verification = database.CreateContext();
        var nodes = await verification
            .Set<TextNode>()
            .OrderBy(node => node.Left)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(129, nodes.Count);
        Assert.Equal(("ROOT", 1L, 258L, 0), (nodes[0].Id, nodes[0].Left, nodes[0].Right, nodes[0].Depth));
        Assert.Equal(
            Enumerable
                .Range(0, 128)
                .Select(value => (long)value),
            nodes
                .Skip(1)
                .Select(node => node.Position));
        Assert.All(
            nodes.Skip(1),
            node =>
            {
                Assert.Equal(
                    ((node.Position * 2) + 2, (node.Position * 2) + 3, 1),
                    (node.Left, node.Right, node.Depth));
                Assert.Equal(parent, node.ParentId);
            });
    }
}
