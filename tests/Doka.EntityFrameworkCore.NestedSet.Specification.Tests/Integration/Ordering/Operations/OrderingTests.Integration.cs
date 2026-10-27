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

    /// <summary>An unregistered ordered facade fails before starting its requested mutation.</summary>
    [Fact]
    public async Task OrderedFacadeRejectsMissingAdapterBeforeMutation()
    {
        // Arrange
        await using var configured = await _fixture.ResetAsync(Engine);
        var connection = configured.Database.GetConnectionString()!;
        var probe = new OrderingSaveProbe();
        var boundary = new ConfigurationIoBoundary();
        var options = new DbContextOptionsBuilder()
            .ConfigureTestWarnings()
            .AddInterceptors(probe, boundary, boundary.Commands);

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
        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            var tree = context.NestedSet<OrderingNode>().ForScope(1);

            await tree.InsertRootAsync(
                new OrderingNode
                {
                    Id = 1,
                    Name = "Root",
                },
                Guid.NewGuid(),
                CancellationToken.None);
        });

        // Assert
        var integrationFailure = Assert.IsType<InvalidOperationException>(exception);

        Assert.Contains("matching NestedSet provider package", integrationFailure.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets()", integrationFailure.Message, StringComparison.Ordinal);
        Assert.Contains("callback", integrationFailure.Message, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.CommandsCreated);
        Assert.Empty(probe.Commands);
        Assert.Empty(
            await configured
                .Set<OrderingNode>()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>A selected adapter still rejects tracked ordering writes saved without the coordinated wrapper.</summary>
    [Fact]
    public async Task TrackedOrderingChangeRejectsMissingCoordinatedSaveWrapper()
    {
        // Arrange
        await using var configured = await _fixture.ResetAsync(Engine);
        await configured
            .NestedSet<OrderingNode>()
            .ForScope(1)
            .InsertRootAsync(
                new OrderingNode
                {
                    Id = 1,
                    Name = "Root",
                },
                Guid.NewGuid(),
                CancellationToken.None);

        var options = ModelCompatibilityDatabase.Options<MissingIntegrationOrderingContext>(
            Engine,
            configured.Database.GetConnectionString()!);

        var probe = new OrderingSaveProbe();
        await using var context = new MissingIntegrationOrderingContext(
            new DbContextOptionsBuilder<MissingIntegrationOrderingContext>(options).AddInterceptors(probe)
                .Options);

        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(row => row.Id == 1, CancellationToken.None);

        node.Name = "Changed";
        probe.Commands.Clear();

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var integrationFailure = Assert.IsType<NestedSetException>(exception);

        Assert.Equal(NestedSetErrorCode.InvalidContext, integrationFailure.Code);
        Assert.Contains("SaveNestedSetChangesAsync", integrationFailure.Message, StringComparison.Ordinal);
        Assert.Empty(probe.Commands);
        Assert.Equal(0, probe.CompletedPayloadUpdates);
        Assert.Equal(0, probe.StructuralUpdates);
        Assert.Equal(EntityState.Modified, context.Entry(node).State);
        Assert.Equal("Changed", node.Name);
        Assert.Equal("Root", (await configured.Set<OrderingNode>().AsNoTracking()
            .SingleAsync(row => row.Id == 1, CancellationToken.None)).Name);
    }
}
