namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingRefreshScaleTests
{
    /// <summary>Native setup preserves every fixture value with one tracked root and bounded write commands.</summary>
    /// <param name="nodes">The number of children, including empty and cross-batch fixtures.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10001)]
    public async Task NativeSeedPreservesEveryRowWithBoundedCommands(
        int nodes
    )
    {
        // Arrange
        var probe = new OrderingSeedProbe();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        await OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, nodes, probe);

        // Assert
        await using var verify = await _fixture.CreateContextAsync(Engine);
        var root = await verify
            .Set<OrderingNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Id == 0, cancellationToken);

        var count = await verify
            .Set<OrderingNode>()
            .LongCountAsync(cancellationToken);

        var registry = NestedSetTreeRegistryMapping.For(verify.Model.FindEntityType(typeof(OrderingNode))!).Registry;
        var registered = await verify
            .Set<NestedSetTreeRegistry>(registry.Name)
            .Select(row => new
            {
                Scope = EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope),
                TreeId = EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                Lifecycle = EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle),
            })
            .ToArrayAsync(cancellationToken);

        var expectedId = 0;

        await foreach (var node in verify
                           .Set<OrderingNode>()
                           .AsNoTracking()
                           .OrderBy(node => node.Id)
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            Assert.Equal(expectedId, node.Id);
            Assert.Equal(1, node.Scope);
            Assert.Equal(root.TreeId, node.TreeId);
            Assert.Equal(node.Id == 0 ? (int?)null : 0, node.ParentId);
            Assert.Equal(node.Id == 0 ? "Root" : $"node-{node.Id:D8}", node.Name);
            Assert.Equal(0, node.Priority);
            Assert.Equal("original", node.Payload);
            Assert.Equal(OrderingCategory.Zeta, node.Category);
            Assert.Equal(node.Id == 0 ? 1L : 2L * node.Id, node.Left);
            Assert.Equal(node.Id == 0 ? 2L * (nodes + 1L) : (2L * node.Id) + 1, node.Right);
            Assert.Equal(node.Id == 0 ? 0 : 1, node.Depth);
            Assert.Equal(node.Id == 0 ? 0L : node.Id - 1L, node.Position);
            expectedId++;
        }

        Assert.Equal(nodes + 1L, count);
        Assert.Equal(nodes + 1, expectedId);
        Assert.NotEqual(Guid.Empty, root.TreeId);
        Assert.Single(registered);
        Assert.Equal(1, registered[0].Scope);
        Assert.Equal(root.TreeId, registered[0].TreeId);
        Assert.Equal(NestedSetTreeRegistryMetadata.Active, registered[0].Lifecycle);
        Assert.Equal(1, probe.MaximumTrackedNodes);
        Assert.Equal(
            (nodes + OrderingRefreshTestSupport.SeedBatchSize - 1) / OrderingRefreshTestSupport.SeedBatchSize,
            probe.NativeWrites);

        Assert.Equal(probe.NativeWrites, probe.CompletedWrites);
        Assert.InRange(probe.MaximumParameters, 0, 7);
        _output.WriteLine(
            $"Engine={Engine}; seed rows={nodes + 1}; native writes={probe.NativeWrites}; "
            + $"maximum tracked nodes={probe.MaximumTrackedNodes}; maximum parameters={probe.MaximumParameters}.");
    }

    /// <summary>A failed native batch rolls back the root, registry and every earlier child write.</summary>
    /// <param name="failOnWrite">The native attempt to reject after root registration or an earlier batch.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task NativeSeedFailureRollsBackEveryEarlierWrite(
        int failOnWrite
    )
    {
        // Arrange
        var probe = new OrderingSeedProbe(failOnWrite);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, 10001, probe));

        // Assert
        await using var verify = await _fixture.CreateContextAsync(Engine);
        var registry = NestedSetTreeRegistryMapping.For(verify.Model.FindEntityType(typeof(OrderingNode))!).Registry;
        Assert.Same(probe.Failure, failure);
        Assert.Equal(failOnWrite, probe.NativeWrites);
        Assert.Equal(failOnWrite - 1, probe.CompletedWrites);
        Assert.Equal(0, await verify
            .Set<OrderingNode>()
            .CountAsync(cancellationToken));

        Assert.Equal(0, await verify
            .Set<NestedSetTreeRegistry>(registry.Name)
            .CountAsync(cancellationToken));
    }

    /// <summary>Invalid cardinalities are rejected before resetting an already populated fixture.</summary>
    [Fact]
    public async Task NativeSeedRejectsNegativeCountBeforeResettingData()
    {
        // Arrange
        await OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, 1);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var before = await _fixture.CreateContextAsync(Engine);
        var treeId = await before
            .Set<OrderingNode>()
            .Select(node => node.TreeId)
            .FirstAsync(cancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            OrderingRefreshTestSupport.SeedAsync(_fixture, Engine, -1));

        // Assert
        await using var verify = await _fixture.CreateContextAsync(Engine);
        Assert.Equal("nodes", failure.ParamName);
        Assert.Equal(2, await verify
            .Set<OrderingNode>()
            .CountAsync(cancellationToken));

        Assert.All(
            await verify
                .Set<OrderingNode>()
                .AsNoTracking()
                .ToArrayAsync(cancellationToken),
            node => Assert.Equal(treeId, node.TreeId));
    }
}
