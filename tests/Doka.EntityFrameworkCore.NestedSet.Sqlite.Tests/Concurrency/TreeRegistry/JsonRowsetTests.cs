namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies SQLite JSON transport for wide tree-registry lock requests.</summary>
public sealed class JsonRowsetTests : ProviderTest, IClassFixture<ProviderFixture<OrderingLockFixture, SqliteEngine>>
{
    private readonly OrderingLockFixture _fixture;

    /// <summary>Uses the SQLite fixture owning this suite's database resources.</summary>
    /// <param name="fixture">The provider fixture owned by this suite or its test collection.</param>
    public JsonRowsetTests(
        ProviderFixture<OrderingLockFixture, SqliteEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Verifies SQLite orders more than five hundred tree identities from one JSON rowset.</summary>
    [Fact]
    public async Task ResolvesMoreThanFiveHundredTreesInOneJsonRowset()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var probe = new OrderingLockProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, probe);
        var requests = Enumerable
            .Range(1, 601)
            .Reverse()
            .Select(scope => OrderingLockTestSupport.Request<int, FirstLockHierarchy>(
                context,
                scope,
                OrderingLockTestSupport.FirstTreeId))
            .ToArray();

        await using var transaction = await OrderingLockTestSupport.BeginAsync(context);

        // Act
        await Execution.NestedSetTreeLocks.AcquireAsync(context, requests, CancellationToken.None);

        // Assert
        var canonical = Assert.Single(OrderingLockTestSupport.CanonicalQueries(probe));
        Assert.Single(canonical.Parameters);
        Assert.Contains("json_each", canonical.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNION ALL", canonical.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            probe.Commands,
            command => command.Sql.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            601,
            OrderingLockTestSupport
                .RegistryLifecycleLocks(probe)
                .Count());
        Assert.Empty(probe.PayloadUpdates);
    }
}
