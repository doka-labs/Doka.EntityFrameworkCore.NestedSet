namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Shares EF singleton services across probe families while retaining independent entity counts.</summary>
    [Fact]
    public async Task ProbeFamiliesShareServiceProviderAndKeepMaterializationCountsIsolated()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(setup));
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var ordinarySetup = database.CreateContext();
        await ordinarySetup
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.NewGuid(), CancellationToken.None);

        var orderedProbe = new OrderingSaveProbe();
        var ordinaryProbe = new EnterpriseProbe();
        await using var ordered = await _fixture.CreateContextAsync(Engine, "Strict", orderedProbe);
        await using var ordinary = database.CreateContext((IInterceptor)ordinaryProbe);

        // Act
        var orderedSingleton = ordered.GetService<IModelSource>();
        var ordinarySingleton = ordinary.GetService<IModelSource>();
        var orderedNodes = await ordered
            .Set<OrderingNode>()
            .AsNoTracking()
            .ToArrayAsync(CancellationToken.None);

        var ordinaryNodes = await ordinary
            .Set<TreeNode>()
            .AsNoTracking()
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Same(orderedSingleton, ordinarySingleton);
        Assert.Equal(5, orderedNodes.Length);
        Assert.Single(ordinaryNodes);
        Assert.Equal(5, orderedProbe.MaterializedNodes);
        Assert.Equal(1, ordinaryProbe.MaterializedNodes);
    }

    /// <summary>Verifies an ordered mutation rejects a context that omitted the required save integration.</summary>
    [Fact]
    public async Task OrderedMutationRejectsMissingSaveIntegration()
    {
        // Arrange
        await using var configured = await _fixture.ResetAsync(Engine);
        var connection = configured.Database.GetConnectionString()!;
        var probe = new OrderingSaveProbe();
        var options = new DbContextOptionsBuilder()
            .ConfigureTestWarnings()
            .AddInterceptors(probe);

        switch (Engine)
        {
            case "Sqlite":
                options.UseSqlite(connection);
                break;
            case "MySql":
                options.UseMySql(connection, DatabaseTestTargets.MySql);
                break;
            case "MariaDb":
                options.UseMySql(connection, DatabaseTestTargets.MariaDb);
                break;
            case "PostgreSql":
                options.UseNpgsql(connection);
                break;
            case "SqlServer":
                options.UseSqlServer(connection);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Engine));
        }

        // WHY: Reusing the configured context's options would also reuse UseNestedSets() and invalidate this
        // negative control. Only the provider connection is intentionally shared.
        await using var context = new MissingIntegrationOrderingContext(options.Options);
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Root",
            },
            Guid.NewGuid(),
            CancellationToken.None));

        // Assert
        var integrationFailure = Assert.IsType<NestedSetException>(exception);
        Assert.Equal(NestedSetErrorCode.InvalidContext, integrationFailure.Code);
        Assert.Contains("UseNestedSets()", integrationFailure.Message, StringComparison.Ordinal);
        Assert.Empty(probe.Commands);
        Assert.Empty(
            await configured
                .Set<OrderingNode>()
                .ToArrayAsync(CancellationToken.None));
    }
}
