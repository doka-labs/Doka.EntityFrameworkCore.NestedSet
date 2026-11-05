using Microsoft.EntityFrameworkCore.Metadata;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Executes an explicitly named tree registry with the actual provider-generated C# model.</summary>
[Collection(AncestorIndexFixtureRegistration.Name)]
public sealed class TreeRegistryCompiledModelTests
{
    private readonly MigrationFixture _fixture;

    /// <summary>Uses the existing native database owner without creating another container fixture.</summary>
    /// <param name="fixture">The shared owner of the selected native engines.</param>
    public TreeRegistryCompiledModelTests(
        MigrationFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Retains the configured name through real code generation and a registry write.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    public async Task GeneratedModelUsesTheConfiguredRegistryTable(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine, qualifySchema: false,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var design = CreateContext(database, engine);
        var designName = Registry(design.GetService<IDesignTimeModel>().Model).GetTableName();
        var model = CompiledModelTestSupport.Generate(design);
        await using var context = CreateContext(database, engine, model);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var tree = Guid.NewGuid();

        // Act
        await context
            .NestedSet<TreeRegistryCompiledNode>()
            .InsertRootAsync(new TreeRegistryCompiledNode { Id = 1 }, tree, CancellationToken.None);

        var registry = Registry(context.Model);
        var rows = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Same(model, context.Model);
        Assert.Equal(TreeRegistryCompiledContext.RegistryTable, designName);
        Assert.Equal(TreeRegistryCompiledContext.RegistryTable, registry.GetTableName());
        Assert.Equal(1, rows);
    }

    /// <summary>Finds the single registry through its public marker type.</summary>
    private static IEntityType Registry(
        IModel model
    ) => model
        .GetEntityTypes()
        .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

    /// <summary>Creates a context for the selected engine with an optional generated model.</summary>
    private static TreeRegistryCompiledContext CreateContext(
        MigrationDatabase database,
        string engine,
        IModel? model = null
    )
    {
        using var source = database.CreateContext();
        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<TreeRegistryCompiledContext>();

        if (engine == "PostgreSql")
        {
            options.UseNpgsql(connection, provider => provider.UseNestedSets());
        }
        else
        {
            options.UseMySql(
                connection,
                engine == "MariaDb" ? DatabaseTestTargets.MariaDb : DatabaseTestTargets.MySql,
                provider => provider.UseNestedSets());
        }

        if (model is not null)
        {
            options.UseModel(model);
        }

        return new TreeRegistryCompiledContext(options.Options);
    }
}
