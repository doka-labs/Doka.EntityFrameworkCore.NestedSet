namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exercises deterministic typed tree-registry locks through real relational providers.</summary>
public abstract class OrderingLockTests : ProviderTest
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
    public async Task RegistryRequestsUseOneNativeOrderAcrossNodeKinds(
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
        await NestedSetTreeLocks.AcquireAsync(context, requests, CancellationToken.None);

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

    /// <summary>Verifies opposite tracked discovery order still locks two trees in one native order.</summary>
    [Fact]
    public async Task ConcurrentSavesLockTreesWithoutInvertingDatabaseOrder()
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
            .SingleAsync(node => node.Id == 22, CancellationToken.None);

        var firstFirstTree = await first
            .Set<OrderingLockNode<int, FirstLockHierarchy>>()
            .SingleAsync(node => node.Id == 12, CancellationToken.None);

        var secondFirstTree = await second
            .Set<OrderingLockNode<int, FirstLockHierarchy>>()
            .SingleAsync(node => node.Id == 12, CancellationToken.None);

        var secondSecondTree = await second
            .Set<OrderingLockNode<int, FirstLockHierarchy>>()
            .SingleAsync(node => node.Id == 22, CancellationToken.None);

        firstSecondTree.Name = firstFirstTree.Name = "Zulu";
        secondFirstTree.Name = secondSecondTree.Name = "Zulu";
        firstProbe.Commands.Clear();
        secondProbe.Commands.Clear();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        // Act
        var saved = await Task.WhenAll(first.SaveChangesAsync(timeout.Token), second.SaveChangesAsync(timeout.Token));

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

    /// <summary>Verifies a missing tree registry rejects a coordinated save before domain payload SQL.</summary>
    [Fact]
    public async Task MissingTreeRegistryRejectsSaveBeforePayloadUpdate()
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
            .SingleAsync(node => node.Id == 12, CancellationToken.None);

        child.Name = "Zulu";
        probe.Commands.Clear();

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            context.SaveChangesAsync(CancellationToken.None));

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
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Verifies binary Scope equality deduplicates separately allocated values on every engine.</summary>
    [Fact]
    public async Task BinaryScopeRequestsUseContentEqualityAndTypedTransport()
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
        await NestedSetTreeLocks.AcquireAsync(context, requests, CancellationToken.None);

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
    public async Task StringScopeRequestsFollowConfiguredDatabaseEquality()
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
        await NestedSetTreeLocks.AcquireAsync(context, requests, CancellationToken.None);

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
    public async Task CanceledAcquisitionDoesNotIssueAnyDatabaseCommand()
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

    /// <summary>Creates one valid root and child in an explicitly identified tree.</summary>
    private static async Task SeedPairAsync<TScope, THierarchy>(
        OrderingLockContext context,
        TScope scope,
        Guid treeId,
        int start,
        CancellationToken cancellationToken
    )
        where TScope : notnull
    {
        var hierarchy = context
            .NestedSet<OrderingLockNode<TScope, THierarchy>>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(
            new OrderingLockNode<TScope, THierarchy>
            {
                Id = start + 1,
                Scope = scope,
                Name = "Root",
            },
            treeId,
            cancellationToken);

        await hierarchy.InsertChildAsync(
            new OrderingLockNode<TScope, THierarchy>
            {
                Id = start + 2,
                Scope = scope,
                Name = "Alpha",
            },
            start + 1,
            cancellationToken);
    }

    /// <summary>Removes one infrastructure row to prove coordinated saves require an active registry.</summary>
    private static async Task DeleteRegistryAsync(
        OrderingLockContext context,
        int scope,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var entityType = context.Model.FindEntityType(typeof(OrderingLockNode<int, FirstLockHierarchy>))!;
        var mapping = NestedSetMapping<OrderingLockNode<int, FirstLockHierarchy>, int, int>.For(context, entityType);
        var registry = NestedSetTreeRegistryMapping.For(mapping.EntityType)
            .Registry;

        var deleted = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .Where(row => EF.Property<int>(row, "Scope") == scope && EF.Property<Guid>(row, "TreeId") == treeId)
            .ExecuteDeleteAsync(cancellationToken);

        Assert.Equal(1, deleted);
    }

    /// <summary>Checks exact persisted structure after both coordinated saves complete.</summary>
    private static async Task AssertPairAsync<TScope, THierarchy>(
        OrderingLockContext context,
        TScope scope,
        Guid treeId,
        int start,
        CancellationToken cancellationToken
    )
        where TScope : notnull
    {
        var nodes = await context
            .NestedSet<OrderingLockNode<TScope, THierarchy>>()
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(cancellationToken);

        Assert.Equal([start + 1, start + 2], nodes.Select(node => node.Id));
        Assert.Equal(["Root", "Zulu"], nodes.Select(node => node.Name));
        Assert.Equal([0, 0], nodes.Select(node => node.Position));
        Assert.Equal([1, 2], nodes.Select(node => node.Left));
        Assert.Equal([4, 3], nodes.Select(node => node.Right));
        Assert.Equal([0, 1], nodes.Select(node => node.Depth));
        Assert.Null(nodes[0].ParentId);
        Assert.Equal(start + 1, nodes[1].ParentId);
    }

    /// <summary>Reads Scope parameters from server registry-lifecycle locking queries.</summary>
    private static int[] IntegerRegistryScopes(
        OrderingLockProbe probe
    ) => OrderingLockTestSupport
        .RegistryLifecycleLocks(probe)
        .Select(command => Assert.IsType<int>(command.Parameters[0].Value))
        .ToArray();
}
