using Microsoft.EntityFrameworkCore.Metadata;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Executes Unicode identity boundaries with the actual provider-generated C# model.</summary>
[Collection(AncestorIndexFixtureRegistration.Name)]
public sealed class UnicodeIdentityCompiledModelTests
{
    private readonly MigrationFixture _fixture;

    /// <summary>Uses the existing native database owner without creating another container fixture.</summary>
    /// <param name="fixture">The shared owner of the selected native engines.</param>
    public UnicodeIdentityCompiledModelTests(
        MigrationFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Retains native character-count mappings through real code generation and root/child writes.</summary>
    /// <param name="engine">The actual character-counting relational provider.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    public async Task GeneratedStringMappingsPreserveSupplementaryIdentityBoundary(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine, qualifySchema: false);
        await using var design = CreateContext(database, engine);
        var model = CompiledModelTestSupport.Generate(design);
        await using var context = CreateContext(database, engine, model);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var suffix = string.Concat(Enumerable.Repeat("\U0001f600", 7));
        var scope = "s" + suffix;
        var tree = "t" + suffix;
        var hierarchy = context
            .NestedSet<UnicodeIdentityCompiledNode>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(new UnicodeIdentityCompiledNode { Id = 1 }, tree, CancellationToken.None);

        // Act
        await hierarchy.InsertChildAsync(new UnicodeIdentityCompiledNode { Id = 2 }, 1, CancellationToken.None);

        // Assert
        var stored = await context
            .Set<UnicodeIdentityCompiledNode>()
            .AsNoTracking()
            .OrderBy(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Same(model, context.Model);
        Assert.Equal(2, stored.Length);
        Assert.All(stored, value => Assert.Equal(scope, value.Scope));
        Assert.All(stored, value => Assert.Equal(tree, value.TreeId));
        Assert.Equal(1, stored[1].ParentId);
        Assert.Equal((1L, 4L, 0), (stored[0].Left, stored[0].Right, stored[0].Depth));
        Assert.Equal((2L, 3L, 1), (stored[1].Left, stored[1].Right, stored[1].Depth));
    }

    /// <summary>Rejects a real scalar overflow for either generated-model identity before reserving a tree.</summary>
    /// <param name="engine">The actual character-counting relational provider.</param>
    /// <param name="scopeRole">Whether the invalid value belongs to Scope rather than TreeId.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    public async Task GeneratedStringMappingsRejectScalarOverflow(
        string engine,
        bool scopeRole
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine, qualifySchema: false);
        await using var design = CreateContext(database, engine);
        var model = CompiledModelTestSupport.Generate(design);
        await using var context = CreateContext(database, engine, model);
        var overflow = string.Concat(Enumerable.Repeat("\U0001f600", 9));
        var hierarchy = context
            .NestedSet<UnicodeIdentityCompiledNode>()
            .ForScope(scopeRole ? overflow : "scope");

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertRootAsync(
            new UnicodeIdentityCompiledNode { Id = 1 },
            scopeRole ? "tree" : overflow,
            CancellationToken.None));

        // Assert
        Assert.Equal(scopeRole ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
        Assert.Same(model, context.Model);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Retains PostgreSQL's explicit bounded alias instead of requiring a canonical type spelling.</summary>
    /// <param name="scopeRole">Whether Scope carries the overflowing value.</param>
    /// <param name="overflow">Whether the native character count exceeds the declared bound.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GeneratedBoundedAliasPreservesCharacterContract(
        bool scopeRole,
        bool overflow
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync("PostgreSql", qualifySchema: false);
        using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<UnicodeIdentityAliasCompiledContext>().UseNpgsql(
            source.Database.GetConnectionString()!,
            provider => provider.UseNestedSets());

        await using var design = new UnicodeIdentityAliasCompiledContext(options.Options);
        var model = CompiledModelTestSupport.Generate(design);
        await using var context = new UnicodeIdentityAliasCompiledContext(
            options.UseModel(model).Options);

        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var suffix = string.Concat(Enumerable.Repeat("\U0001f600", 7));
        var scope = "s" + suffix + (overflow && scopeRole ? "x" : string.Empty);
        var tree = "t" + suffix + (overflow && !scopeRole ? "x" : string.Empty);

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<UnicodeIdentityCompiledNode>()
            .ForScope(scope)
            .InsertRootAsync(new UnicodeIdentityCompiledNode { Id = 1 }, tree, CancellationToken.None));

        // Assert
        Assert.Same(model, context.Model);
        var entity = context.Model.FindEntityType(typeof(UnicodeIdentityCompiledNode))!;
        Assert.Equal("bpchar", entity.FindProperty(nameof(UnicodeIdentityCompiledNode.Scope))!
            .GetRelationalTypeMapping().StoreTypeNameBase);
        Assert.Equal("bpchar", entity.FindProperty(nameof(UnicodeIdentityCompiledNode.TreeId))!
            .GetRelationalTypeMapping().StoreTypeNameBase);

        if (overflow)
        {
            Assert.Equal(scopeRole ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
            Assert.Empty(await context.Set<UnicodeIdentityCompiledNode>().ToArrayAsync(CancellationToken.None));
        }
        else
        {
            Assert.Null(error);
            var stored = await context.Set<UnicodeIdentityCompiledNode>().SingleAsync(CancellationToken.None);
            Assert.Equal(scope, stored.Scope);
            Assert.Equal(tree, stored.TreeId);
        }
    }

    /// <summary>Uses the real fixture connection with the exact supplied runtime model when present.</summary>
    private static UnicodeIdentityCompiledContext CreateContext(
        MigrationDatabase database,
        string engine,
        IModel? model = null
    )
    {
        using var source = database.CreateContext();
        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<UnicodeIdentityCompiledContext>();

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

        return new UnicodeIdentityCompiledContext(options.Options);
    }
}
