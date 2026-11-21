namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exercises deterministic typed tree-registry locks through real relational providers.</summary>
public abstract partial class OrderingLockTests : ProviderTest
{
    private static readonly Guid s_secondTreeId = Guid.Parse("6182f693-b9c7-4229-a1de-d4ff2da2eca7");
    private static readonly int[] s_integerScopes = [1, 2];
    private static readonly string[] s_binaryScopes = ["01", "02"];
    private static readonly string[] s_stringScopes = ["a", "b"];

    private readonly OrderingLockFixture _fixture;

    /// <summary>Creates a case using isolated registry mappings on real relational engines.</summary>
    /// <param name="fixture">The fixture owning the isolated provider database and test tables.</param>
    protected OrderingLockTests(
        IProviderFixture<OrderingLockFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Verifies request order cannot reverse or duplicate registry locks across hierarchy types.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcquireAsync_RegistryRequests_UseOneNativeOrderAcrossNodeKinds(
        bool reverseScopes
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new OrderingLockProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var firstScope = reverseScopes
            ? 1
            : 2;

        var secondScope = reverseScopes
            ? 2
            : 1;

        var first = OrderingLockTestSupport.Request<int, FirstLockHierarchy>(
            context,
            firstScope,
            OrderingLockTestSupport.FirstTreeId);

        var second = OrderingLockTestSupport.Request<int, SecondLockHierarchy>(context, secondScope, s_secondTreeId);
        INestedSetTreeLockRequest[] requests = [first, second, second, first];
        await using var transaction = await OrderingLockTestSupport.BeginAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await NestedSetTreeLocks.AcquireAsync(context, requests, TestContext.Current.CancellationToken);

        // Assert
        var canonical = OrderingLockTestSupport
            .CanonicalQueries(probe)
            .ToArray();

        Assert.Equal(2, canonical.Length);
        Assert.All(
            canonical,
            command => Assert.Equal(
                Engine == "SqlServer"
                    ? 1
                    : 4,
                command.Parameters.Length));

        Assert.Equal(
            2,
            OrderingLockTestSupport
                .RegistryLifecycleLocks(probe)
                .Count());

        Assert.Empty(probe.PayloadUpdates);
    }

    /// <summary>Verifies a missing tree registry rejects a coordinated save before domain payload SQL.</summary>
    [Fact]
    public async Task SaveChangesAsync_MissingTreeRegistry_RejectsSaveBeforePayloadUpdate()
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

        await DeleteRegistryAsync(
            setup,
            1,
            OrderingLockTestSupport.FirstTreeId,
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new OrderingLockProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var child = await context
            .Set<OrderingLockNode<int, FirstLockHierarchy>>()
            .SingleAsync(node => node.Id == 12, TestContext.Current.CancellationToken);

        child.Name = "Zulu";
        probe.Commands.Clear();

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeNotFound, error.Code);
        Assert.Equal("The selected tree has no active registry row.", error.Message);
        Assert.Empty(probe.PayloadUpdates);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(child)
                .State);

        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            "Alpha",
            await verification
                .Set<OrderingLockNode<int, FirstLockHierarchy>>()
                .Where(node => node.Id == 12)
                .Select(node => node.Name)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Verifies binary Scope equality deduplicates separately allocated values on every engine.</summary>
    [Fact]
    public async Task AcquireAsync_BinaryScopeRequests_UseContentEqualityAndTypedTransport()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new OrderingLockProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        NestedSetTreeLockRequest<Guid, byte[]>[] requests =
        [
            OrderingLockTestSupport.Request<byte[], FirstLockHierarchy>(
                context,
                [2],
                OrderingLockTestSupport.FirstTreeId),
            OrderingLockTestSupport.Request<byte[], FirstLockHierarchy>(
                context,
                [1],
                OrderingLockTestSupport.FirstTreeId),
            OrderingLockTestSupport.Request<byte[], FirstLockHierarchy>(
                context,
                [2],
                OrderingLockTestSupport.FirstTreeId),
            OrderingLockTestSupport.Request<byte[], FirstLockHierarchy>(
                context,
                [1],
                OrderingLockTestSupport.FirstTreeId),
        ];

        await using var transaction = await OrderingLockTestSupport.BeginAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await NestedSetTreeLocks.AcquireAsync(context, requests, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotSame(requests[0].Scope, requests[2].Scope);
        var canonical = Assert.Single(OrderingLockTestSupport.CanonicalQueries(probe));
        Assert.Equal(
            Engine == "SqlServer"
                ? 1
                : 8,
            canonical.Parameters.Length);

        Assert.Equal(
            s_binaryScopes,
            OrderingLockTestSupport
                .RegistryLifecycleLocks(probe)
                .Select(command => Convert.ToHexString(Assert.IsType<byte[]>(command.Parameters[0].Value))));

        Assert.Empty(probe.PayloadUpdates);
    }

    /// <summary>Verifies explicit string collations resolve aliases and deduplicate their registry locks.</summary>
    [Fact]
    public async Task AcquireAsync_StringScopeRequests_FollowConfiguredDatabaseEquality()
    {
        // Arrange
        var caseInsensitive = Engine != "PostgreSql";

        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new OrderingLockProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        NestedSetTreeLockRequest<Guid, string>[] requests =
        [
            OrderingLockTestSupport.Request<string, FirstLockHierarchy>(
                context,
                "b",
                OrderingLockTestSupport.FirstTreeId),
            OrderingLockTestSupport.Request<string, FirstLockHierarchy>(
                context,
                "a",
                OrderingLockTestSupport.FirstTreeId),
            OrderingLockTestSupport.Request<string, FirstLockHierarchy>(
                context,
                caseInsensitive
                    ? "A"
                    : "a",
                OrderingLockTestSupport.FirstTreeId),
            OrderingLockTestSupport.Request<string, FirstLockHierarchy>(
                context,
                caseInsensitive
                    ? "B"
                    : "b",
                OrderingLockTestSupport.FirstTreeId),
        ];

        await using var transaction = await OrderingLockTestSupport.BeginAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await NestedSetTreeLocks.AcquireAsync(context, requests, TestContext.Current.CancellationToken);

        // Assert
        var canonical = Assert.Single(OrderingLockTestSupport.CanonicalQueries(probe));
        Assert.Contains("COLLATE", canonical.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            Engine == "SqlServer"
                ? 1
                : 8,
            canonical.Parameters.Length);

        Assert.Equal(
            s_stringScopes,
            OrderingLockTestSupport
                .RegistryLifecycleLocks(probe)
                .Select(command => Assert.IsType<string>(command.Parameters[0].Value)));

        Assert.Empty(probe.PayloadUpdates);
    }

    /// <summary>Verifies cancellation at entry prevents registry resolution and locking on every provider.</summary>
    [Fact]
    public async Task AcquireAsync_CanceledAcquisition_DoesNotIssueAnyDatabaseCommand()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new OrderingLockProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var request = OrderingLockTestSupport.Request<int, FirstLockHierarchy>(
            context,
            1,
            OrderingLockTestSupport.FirstTreeId);

        await using var transaction = await OrderingLockTestSupport.BeginAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NestedSetTreeLocks.AcquireAsync(context, [request], cancellation.Token));

        // Assert
        Assert.Empty(probe.Commands);
    }
}
