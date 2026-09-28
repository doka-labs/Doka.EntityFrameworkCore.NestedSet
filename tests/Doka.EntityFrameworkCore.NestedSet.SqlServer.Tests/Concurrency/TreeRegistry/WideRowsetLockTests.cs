namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>
/// Verifies SQL Server orders tree-registry requests above its parameter ceiling through one JSON rowset.
/// </summary>
public sealed class WideRowsetLockTests : ProviderTest,
    IClassFixture<ProviderFixture<OrderingLockFixture, SqlServerEngine>>
{
    private readonly OrderingLockFixture _fixture;

    /// <summary>Creates cases using typed tree registries and an isolated class-owned database.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    public WideRowsetLockTests(
        ProviderFixture<OrderingLockFixture, SqlServerEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Verifies one database-defined order survives more trees than SQL Server's parameter ceiling.</summary>
    [Fact]
    public async Task RequestedTreesAboveParameterLimitUseOneJsonRowset()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var probe = new OrderingLockProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, probe);
        var mapping = Mapping.NestedSetMapping<OrderingLockNode<int, FirstLockHierarchy>, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(OrderingLockNode<int, FirstLockHierarchy>))!);

        var treeId = Guid.Parse("53b75272-c9ef-4094-81d0-5cc8e994128d");
        var requests = Enumerable
            .Range(1, 2105)
            .Reverse()
            .Select(scope => new Execution.NestedSetTreeLockRequest<Guid, int>(
                mapping.EntityType,
                scope,
                treeId,
                Execution.NestedSetTreeLockMode.New))
            .ToArray();

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken.None);

        // Act
        await Execution.NestedSetTreeLocks.AcquireAsync(context, requests, CancellationToken.None);

        // Assert
        var resolution = Assert.Single(
            probe.Commands,
            command => command.Sql.Contains("RequestedTrees", StringComparison.Ordinal));

        Assert.Single(resolution.Parameters);
        Assert.Contains("OPENJSON", resolution.Sql, StringComparison.Ordinal);
        var lifecycleLocks = probe.Commands.Where(command =>
            command.Sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && command.Sql.Contains("Lifecycle", StringComparison.Ordinal));

        Assert.Equal(
            Enumerable.Range(1, 2105),
            lifecycleLocks.Select(command => Assert.IsType<int>(command.Parameters[0].Value)));
    }
}
