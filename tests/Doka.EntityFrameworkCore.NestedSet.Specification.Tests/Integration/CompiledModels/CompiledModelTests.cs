namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Verifies generated metadata is reused without rebuilding a design model during hierarchy operations.
/// </summary>
public abstract class CompiledModelTests : ProviderTest
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
    [Fact]
    public void DesignModelUsesTypedTreeRegistries()
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
    [Fact]
    public void CompiledRegistryNamesMatchDesignModel()
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
    [Fact]
    public void CompiledStringIdentityCapturesMatchDesignModel()
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
        var generated = ModelFor(Engine).FindEntityType(typeof(CompiledFolder))!;
        var names = new[] { nameof(CompiledFolder.Id), nameof(CompiledFolder.Scope), nameof(CompiledFolder.ParentId), };

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
            Assert.Equal(scopeCapture, ordinary.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);
        }
    }

    /// <summary>
    ///     Verifies string aliases, parent collation and automatic ordered saves work on generated models.
    /// </summary>
    [Fact]
    public async Task StringHierarchyUsesCapturedCollationsWithoutRebuildingTheModel()
    {
        // Arrange
        var model = ModelFor(Engine);
        await using var context = await _fixture.ResetAsync(Engine, model);
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
    public async Task NumericHierarchyUsesTheSameGeneratedModel()
    {
        // Arrange
        var model = ModelFor(Engine);
        await using var context = await _fixture.ResetAsync(Engine, model);
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
    public async Task ResetClearsPopulatedCompiledHierarchies()
    {
        // Arrange
        var model = ModelFor(Engine);

        await using (var seeded = await _fixture.ResetAsync(Engine, model))
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
        await using var reset = await _fixture.ResetAsync(Engine, model);

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

    /// <summary>Verifies explicitly supplied models fail clearly if finalization capture was omitted.</summary>
    [Fact]
    public async Task SuppliedModelWithoutCollationCaptureHasAnActionableDiagnostic()
    {
        // Arrange
        var options = await _fixture.CreateOptionsAsync<LegacyCompiledContext>(
            Engine,
            model: null,
            enableNestedSets: false);

        await using var source = new LegacyCompiledContext(options);
        var supplied = source.Model;
        await using var context = new LegacyCompiledContext(
            new DbContextOptionsBuilder<LegacyCompiledContext>(options)
                .ConfigureTestWarnings()
                .UseModel(supplied)
                .Options);

        // Act
        var exception = Record.Exception(() => context
            .NestedSet<TextNode>()
            .ForScope("scope"));

        // Assert
        var error = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("optionsBuilder.UseNestedSets()", error.Message, StringComparison.Ordinal);
        Assert.Contains("before model generation", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects stale property-only capture with an actionable regeneration diagnostic.</summary>
    [Fact]
    public async Task SuppliedPropertyOnlyCaptureRequiresRegeneration()
    {
        // Arrange
        var options = await _fixture.CreateOptionsAsync<StaleCollationCaptureContext>(
            Engine,
            model: null,
            enableNestedSets: false);

        await using var source = new StaleCollationCaptureContext(options);
        var supplied = source.Model;
        await using var context = new StaleCollationCaptureContext(
            new DbContextOptionsBuilder<StaleCollationCaptureContext>(options).UseModel(supplied).Options);

        var owner = supplied.FindEntityType(typeof(TextNode))!;
        var key = owner.FindProperty(nameof(TextNode.Id))!;

        // Act
        var failure = Record.Exception(() => NestedSetCollations.Resolve(context, key, owner));

        // Assert
        var error = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("regenerat", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(supplied, context.Model);
        Assert.True((bool)supplied.FindAnnotation(NestedSetAnnotationNames.CollationsCaptured)!.Value!);
        Assert.Equal(string.Empty, key.FindAnnotation(NestedSetAnnotationNames.Collation)?.Value);
        Assert.Null(owner.FindAnnotation(NestedSetAnnotationNames.Collation + ":" + key.Name));
    }

    /// <summary>
    ///     Selects actual checked-in generator output rather than constructing a runtime model in the test.
    /// </summary>
    private static IModel ModelFor(
        string engine
    ) => engine switch
    {
        "Sqlite" => CompiledModels.CompiledTreeContextModel.Instance,
        "MySql" => CompiledModels.MySql.CompiledMySqlContextModel.Instance,
        "MariaDb" => CompiledModels.MariaDb.CompiledMariaDbContextModel.Instance,
        "PostgreSql" => CompiledModels.PostgreSql.CompiledPostgreSqlContextModel.Instance,
        "SqlServer" => CompiledModels.SqlServer.CompiledSqlServerContextModel.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };
}
