namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingLockTests
{
    /// <summary>Verifies opposite tracked discovery order still locks two trees in one native order.</summary>
    [Fact]
    public async Task SaveChangesAsync_ConcurrentSaves_LockTreesWithoutInvertingDatabaseOrder()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPairAsync<int, FirstLockHierarchy>(
            setup,
            1,
            OrderingLockTestSupport.FirstTreeId,
            10,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPairAsync<int, FirstLockHierarchy>(
            setup,
            2,
            s_secondTreeId,
            20,
            cancellationToken: TestContext.Current.CancellationToken);

        var barrier = Engine == "Sqlite"
            ? null
            : new OrderingLockBarrier();

        var firstProbe = new OrderingLockProbe { FirstAnchorBarrier = barrier };
        var secondProbe = new OrderingLockProbe { FirstAnchorBarrier = barrier };
        await using var first = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            firstProbe);

        await using var second = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            secondProbe);

        var firstSecondTree = await first
            .Set<OrderingLockNode<int, FirstLockHierarchy>>()
            .SingleAsync(node => node.Id == 22, TestContext.Current.CancellationToken);

        var firstFirstTree = await first
            .Set<OrderingLockNode<int, FirstLockHierarchy>>()
            .SingleAsync(node => node.Id == 12, TestContext.Current.CancellationToken);

        var secondFirstTree = await second
            .Set<OrderingLockNode<int, FirstLockHierarchy>>()
            .SingleAsync(node => node.Id == 12, TestContext.Current.CancellationToken);

        var secondSecondTree = await second
            .Set<OrderingLockNode<int, FirstLockHierarchy>>()
            .SingleAsync(node => node.Id == 22, TestContext.Current.CancellationToken);

        firstSecondTree.Name = firstFirstTree.Name = "Zulu";
        secondFirstTree.Name = secondSecondTree.Name = "Zulu";
        firstProbe.Commands.Clear();
        secondProbe.Commands.Clear();
        var operationCancellationToken = TestContext.Current.CancellationToken;

        // Act
        var saved = await Task.WhenAll(
            first.SaveChangesAsync(operationCancellationToken),
            second.SaveChangesAsync(operationCancellationToken));

        // Assert
        Assert.Equal([2, 2], saved);
        Assert.Equal(
            Engine == "Sqlite"
                ? 0
                : 2,
            barrier?.Arrivals ?? 0);

        Assert.Equal(s_integerScopes, IntegerRegistryScopes(firstProbe));
        Assert.Equal(s_integerScopes, IntegerRegistryScopes(secondProbe));
        Assert.Single(OrderingLockTestSupport.CanonicalQueries(firstProbe));
        Assert.Single(OrderingLockTestSupport.CanonicalQueries(secondProbe));
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await AssertPairAsync<int, FirstLockHierarchy>(
            verification,
            1,
            OrderingLockTestSupport.FirstTreeId,
            10,
            cancellationToken: TestContext.Current.CancellationToken);

        await AssertPairAsync<int, FirstLockHierarchy>(
            verification,
            2,
            s_secondTreeId,
            20,
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
