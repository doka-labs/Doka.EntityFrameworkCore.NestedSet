using Doka.EntityFrameworkCore.NestedSet.Tests.CompiledModels;
using Doka.EntityFrameworkCore.NestedSet.Tests.CompiledModels.MariaDb;
using Doka.EntityFrameworkCore.NestedSet.Tests.CompiledModels.MySql;
using Doka.EntityFrameworkCore.NestedSet.Tests.CompiledModels.PostgreSql;
using Doka.EntityFrameworkCore.NestedSet.Tests.CompiledModels.SqlServer;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Verifies generated metadata is reused without rebuilding a design model during hierarchy operations.
/// </summary>
public abstract partial class CompiledModelTests : ProviderTest
{
    private readonly CompiledModelFixture _fixture;

    /// <summary>Creates the provider matrix over independently owned relational databases.</summary>
    protected CompiledModelTests(
        IProviderFixture<CompiledModelFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Captures one typed registry per hierarchy in every provider-specific generated model.</summary>
    [DatabaseIndependent]
    [Fact]
    public void GetEntityTypes_DesignModel_UsesTypedTreeRegistries()
    {
        // Arrange
        using DbContext context = Engine switch
        {
            "Sqlite" => new CompiledTreeContext(),
            "MySql" => new CompiledMySqlContext(),
            "MariaDb" => new CompiledMariaDbContext(),
            "PostgreSql" => new CompiledPostgreSqlContext(),
            "SqlServer" => new CompiledSqlServerContext(),
            _ => throw new ArgumentOutOfRangeException(nameof(Engine)),
        };

        // Act
        var registries = context
            .Model
            .GetEntityTypes()
            .Where(entity => entity.ClrType == typeof(NestedSetTreeRegistry))
            .ToArray();

        var script = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal(2, registries.Length);
        Assert.All(
            registries,
            registry =>
            {
                Assert.Equal(
                    ["Scope", "TreeId"],
                    registry.FindPrimaryKey()!.Properties.Select(property => property.Name));

                Assert.Contains(registry.GetTableName()!, script, StringComparison.Ordinal);
            });

        Assert.DoesNotContain("DokaNestedSetLocks", script, StringComparison.Ordinal);
    }

    /// <summary>Keeps checked-in provider models synchronized with the current registry naming convention.</summary>
    [DatabaseIndependent]
    [Fact]
    public void GetEntityTypes_CompiledRegistryNames_MatchDesignModel()
    {
        // Arrange
        using DbContext context = Engine switch
        {
            "Sqlite" => new CompiledTreeContext(),
            "MySql" => new CompiledMySqlContext(),
            "MariaDb" => new CompiledMariaDbContext(),
            "PostgreSql" => new CompiledPostgreSqlContext(),
            "SqlServer" => new CompiledSqlServerContext(),
            _ => throw new ArgumentOutOfRangeException(nameof(Engine)),
        };

        // Act
        var designNames = context
            .Model
            .GetEntityTypes()
            .Where(entity => entity.ClrType == typeof(NestedSetTreeRegistry))
            .Select(entity => entity.GetTableName())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var compiledNames = ModelFor(Engine)
            .GetEntityTypes()
            .Where(entity => entity.ClrType == typeof(NestedSetTreeRegistry))
            .Select(entity => entity.GetTableName())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Equal(2, designNames.Length);
        Assert.Equal(designNames, compiledNames);
    }

    /// <summary>Checks each generated hierarchy retains effective string facets, including table defaults.</summary>
    [DatabaseIndependent]
    [Fact]
    public void FindAnnotation_CompiledStringIdentity_CapturesMatchDesignModel()
    {
        // Arrange
        using DbContext context = Engine switch
        {
            "Sqlite" => new CompiledTreeContext(),
            "MySql" => new CompiledMySqlContext(),
            "MariaDb" => new CompiledMariaDbContext(),
            "PostgreSql" => new CompiledPostgreSqlContext(),
            "SqlServer" => new CompiledSqlServerContext(),
            _ => throw new ArgumentOutOfRangeException(nameof(Engine)),
        };

        var design = context.Model.FindEntityType(typeof(CompiledFolder))!;
        var generated = ModelFor(Engine)
            .FindEntityType(typeof(CompiledFolder))!;

        var names = new[] { nameof(CompiledFolder.Id), nameof(CompiledFolder.Scope), nameof(CompiledFolder.ParentId) };

        // Act
        var captures = names
            .Select(name => (Design: design.FindAnnotation(NestedSetAnnotationNames.Collation + ":" + name)
                ?.Value, Generated: generated.FindAnnotation(NestedSetAnnotationNames.Collation + ":" + name)
                ?.Value))
            .ToArray();

        // Assert
        Assert.All(
            captures,
            capture =>
            {
                Assert.IsType<string>(capture.Design);
                Assert.Equal(capture.Design, capture.Generated);
            });

        var scopeCapture = Assert.IsType<string>(captures[1].Generated);
        Assert.Equal(captures[0].Generated, scopeCapture);
        Assert.NotEqual(scopeCapture, captures[2].Generated);
        if (Engine is "MySql" or "MariaDb")
        {
            Assert.Equal("utf8mb4_unicode_ci", scopeCapture);
            var ordinary = context
                .GetService<IDesignTimeModel>()
                .Model
                .FindEntityType(typeof(CompiledFolder))!;

            Assert.Null(ordinary.FindProperty(nameof(CompiledFolder.Scope))!.GetCollation());
            Assert.Equal(
                scopeCapture,
                ordinary.FindAnnotation(RelationalAnnotationNames.Collation)
                    ?.Value);
        }
    }

    /// <summary>
    ///     Verifies string aliases, parent collation and automatic ordered saves work on generated models.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_StringHierarchy_UsesCapturedCollationsWithoutRebuildingTheModel()
    {
        // Arrange
        var model = ModelFor(Engine);
        await using var context = await _fixture.ResetAsync(
            Engine,
            model,
            cancellationToken: TestContext.Current.CancellationToken);

        var tree = context
            .NestedSet<CompiledFolder>()
            .ForScope("SCOPE");

        await tree.InsertRootAsync(
            new CompiledFolder
            {
                Id = "ROOT",
                Scope = "scope",
                Name = "Root",
            },
            Guid.Empty,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new CompiledFolder
            {
                Id = "B",
                Scope = "scope",
                Name = "Beta",
            },
            "root",
            CancellationToken.None);

        await tree.InsertChildAsync(
            new CompiledFolder
            {
                Id = "A",
                Scope = "scope",
                Name = "Alpha",
            },
            "root",
            CancellationToken.None);

        var changed = await context
            .Set<CompiledFolder>()
            .SingleAsync(row => row.Id == "a", CancellationToken.None);

        changed.Name = "Zulu";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var rows = await tree
            .TreeContaining("b")
            .ToArrayAsync(CancellationToken.None);

        var parent = await tree
            .ParentOf("b")
            .SingleAsync(CancellationToken.None);

        var problems = await tree
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Same(model, context.Model);
        Assert.Equal(["ROOT", "B", "A"], rows.Select(row => row.Id));
        Assert.Equal("ROOT", parent.Id);
        Assert.Empty(problems.Issues);
    }

    /// <summary>Verifies numeric-key projections and mutations reuse each provider's generated runtime model.</summary>
    [Fact]
    public async Task InsertChildAsync_NumericHierarchy_UsesTheSameGeneratedModel()
    {
        // Arrange
        var model = ModelFor(Engine);
        await using var context = await _fixture.ResetAsync(
            Engine,
            model,
            cancellationToken: TestContext.Current.CancellationToken);

        var tree = context
            .NestedSet<CompiledNumber>()
            .ForScope(7);

        await tree.InsertRootAsync(
            new CompiledNumber
            {
                Id = 1,
                Scope = 7,
            },
            Guid.Empty,
            CancellationToken.None);

        // Act
        await tree.InsertChildAsync(
            new CompiledNumber
            {
                Id = 2,
                Scope = 7,
            },
            1,
            CancellationToken.None);

        var ids = await tree
            .TreeContaining(2)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        var problems = await tree
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Same(model, context.Model);
        Assert.Equal([1, 2], ids);
        Assert.Empty(problems.Issues);
    }

    /// <summary>Fixture reuse clears populated self-referencing hierarchies before the next case starts.</summary>
    [Fact]
    public async Task ResetAsync_Reset_ClearsPopulatedCompiledHierarchies()
    {
        // Arrange
        var model = ModelFor(Engine);

        await using (var seeded = await _fixture.ResetAsync(
            Engine,
            model,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            var folders = seeded
                .NestedSet<CompiledFolder>()
                .ForScope("scope");

            var numbers = seeded
                .NestedSet<CompiledNumber>()
                .ForScope(7);

            await folders.InsertRootAsync(
                new CompiledFolder
                {
                    Id = "ROOT",
                    Scope = "scope",
                    Name = "Root",
                },
                Guid.Empty,
                CancellationToken.None);

            await folders.InsertChildAsync(
                new CompiledFolder
                {
                    Id = "CHILD",
                    Scope = "scope",
                    Name = "Child",
                },
                "ROOT",
                CancellationToken.None);

            await numbers.InsertRootAsync(
                new CompiledNumber
                {
                    Id = 1,
                    Scope = 7,
                },
                Guid.Empty,
                CancellationToken.None);

            await numbers.InsertChildAsync(
                new CompiledNumber
                {
                    Id = 2,
                    Scope = 7,
                },
                1,
                CancellationToken.None);
        }

        // Act
        await using var reset = await _fixture.ResetAsync(
            Engine,
            model,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(
            await reset
                .Set<CompiledFolder>()
                .ToArrayAsync(CancellationToken.None));

        Assert.Empty(
            await reset
                .Set<CompiledNumber>()
                .ToArrayAsync(CancellationToken.None));

        Assert.Single(
            await reset
                .Set<CompiledScope>()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Selects actual checked-in generator output rather than constructing a runtime model in the test.
    /// </summary>
    private static IModel ModelFor(
        string engine
    ) => engine switch
    {
        "Sqlite" => CompiledTreeContextModel.Instance,
        "MySql" => CompiledMySqlContextModel.Instance,
        "MariaDb" => CompiledMariaDbContextModel.Instance,
        "PostgreSql" => CompiledPostgreSqlContextModel.Instance,
        "SqlServer" => CompiledSqlServerContextModel.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };
}
