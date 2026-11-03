namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies explicit tree-registry table names, their diagnostics and the unchanged default.</summary>
public sealed class TreeRegistryTableNameTests
{
    /// <summary>Keeps the exact generated name when no explicit name is configured.</summary>
    [Fact]
    public void DefaultNameIsUnchangedWithoutTheOption()
    {
        // Arrange
        using var context = Create(modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", null));

        // Act
        var registry = Registry<FirstNode>(context);

        // Assert
        Assert.Equal(
            NestedSetTreeRegistryMetadata.TableName(null, "Firsts", "Id", null, "TreeId"),
            registry.GetTableName());
    }

    /// <summary>Applies the explicit name to its own hierarchy and retains the schema and identity.</summary>
    [Fact]
    public void ExplicitNameAppliesToItsHierarchyOnly()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Reporting_HierarchyTrees");
            Map<SecondNode>(modelBuilder, "Seconds", "Organization_HierarchyTrees");
            Map<ThirdNode>(modelBuilder, "Thirds", null);
        });

        // Act
        var first = Registry<FirstNode>(context);
        var second = Registry<SecondNode>(context);
        var third = Registry<ThirdNode>(context);

        // Assert
        Assert.Equal("Reporting_HierarchyTrees", first.GetTableName());
        Assert.Equal("Organization_HierarchyTrees", second.GetTableName());
        Assert.StartsWith("DokaNestedSetTrees_", third.GetTableName(), StringComparison.Ordinal);
        Assert.Equal(["TreeId"], first.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(NestedSetTreeRegistryMetadata.EntityName(typeof(FirstNode).FullName!), first.Name);
    }

    /// <summary>Keeps the hierarchy's schema, because this option does not move the registry.</summary>
    [Fact]
    public void ExplicitNameStaysInTheHierarchySchema()
    {
        // Arrange
        using var context = Create(modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", "Reporting", "tenant"));

        // Act
        var registry = Registry<FirstNode>(context);

        // Assert
        Assert.Equal("Reporting", registry.GetTableName());
        Assert.Equal("tenant", registry.GetSchema());
    }

    /// <summary>Rejects values that are not one unqualified identifier at the call site.</summary>
    /// <param name="name">The invalid table name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Leading")]
    [InlineData("Trailing ")]
    [InlineData("dbo.Trees")]
    [InlineData("Tr\"ees")]
    [InlineData("Tr`ees")]
    [InlineData("[Trees]")]
    [InlineData("Tr'ees")]
    [InlineData("Tre\nes")]
    public void InvalidNamesAreRejectedAtTheBuilder(
        string name
    )
    {
        // Arrange
        using var context = Create(modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", name));

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.IsType<ArgumentException>(error, exactMatch: false);
    }

    /// <summary>Publishes nothing when a later callback step fails.</summary>
    [Fact]
    public void FailedCallbackLeavesNoAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var node = modelBuilder.Entity<FirstNode>();

        // Act
        var error = Record.Exception(() => node.HasNestedSet(nestedSet =>
        {
            nestedSet.HasTreeRegistryTable("Valid");

            throw new InvalidOperationException("Later configuration failed.");
        }));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Null(node.Metadata.FindAnnotation(NestedSetAnnotationNames.TreeRegistryTable));
    }

    /// <summary>Rejects two registries that would share one table, including a case-only difference.</summary>
    /// <param name="otherName">The second hierarchy's explicit name.</param>
    [Theory]
    [InlineData("Shared")]
    [InlineData("shared")]
    public void RegistryCollisionIsRejectedWithBothNames(
        string otherName
    )
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Shared");
            Map<SecondNode>(modelBuilder, "Seconds", otherName);
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("collides", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a name already used by an application table.</summary>
    [Fact]
    public void ApplicationTableCollisionIsRejected()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Orders");
            modelBuilder
                .Entity<Order>()
                .ToTable("Orders");
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Orders", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a name equal to the hierarchy's own table.</summary>
    [Fact]
    public void OwnHierarchyTableCollisionIsRejected()
    {
        // Arrange
        using var context = Create(modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", "Firsts"));

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Allows the same name in another schema where the provider creates schemas.</summary>
    /// <param name="provider">A provider whose DDL keeps the schema.</param>
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("Postgres")]
    [InlineData("MySql")]
    public void SameNameInAnotherSchemaIsAllowedWhereSchemasExist(
        string provider
    )
    {
        // Arrange
        using var context = CreateFor(
            provider,
            modelBuilder =>
            {
                Map<FirstNode>(modelBuilder, "Firsts", "Trees", "first");
                Map<SecondNode>(modelBuilder, "Seconds", "Trees", "second");
            });

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;

        var first = NestedSetTreeRegistryMapping.For(model.FindEntityType(typeof(FirstNode))!).Registry;

        var second = NestedSetTreeRegistryMapping.For(model.FindEntityType(typeof(SecondNode))!).Registry;

        // Assert
        Assert.Equal(first.GetTableName(), second.GetTableName());
        Assert.NotEqual(first.GetSchema(), second.GetSchema());
    }

    /// <summary>Rejects one name in two schemas on SQLite, whose DDL drops schemas and creates one table.</summary>
    [Fact]
    public void SameNameInAnotherSchemaCollidesOnSqlite()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Trees", "first");
            Map<SecondNode>(modelBuilder, "Seconds", "Trees", "second");
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("collides", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Compares schema names like table names, so case-only schema differences follow the provider.</summary>
    /// <param name="provider">The provider under test.</param>
    /// <param name="collides">Whether that provider can treat both schemas as one.</param>
    [Theory]
    [InlineData("SqlServer", true)]
    [InlineData("MySql", true)]
    [InlineData("Postgres", false)]
    public void SchemaCaseVariantFollowsTheProvider(
        string provider,
        bool collides
    )
    {
        // Arrange
        using var context = CreateFor(
            provider,
            modelBuilder =>
            {
                Map<FirstNode>(modelBuilder, "Firsts", "Trees", "Tenant");
                Map<SecondNode>(modelBuilder, "Seconds", "Trees", "tenant");
            });

        // Act
        var error = Record.Exception(() => _ = context.GetService<IDesignTimeModel>().Model);

        // Assert
        if (collides)
        {
            Assert.Contains(
                "collides",
                Assert.IsType<InvalidOperationException>(error).Message);
        }
        else
        {
            Assert.Null(error);
        }
    }

    /// <summary>Keeps names apart that SQLite itself keeps apart, because it folds only ASCII letters.</summary>
    /// <returns>A task that completes after the native schema names have been inspected.</returns>
    [Fact]
    public async Task SqliteFoldsOnlyAsciiLetters()
    {
        // Arrange
        await using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "\u00c4Trees");
            Map<SecondNode>(modelBuilder, "Seconds", "\u00e4Trees");
            Map<ThirdNode>(modelBuilder, "Thirds", "\u017fqlite_Trees");
        });

        await context.Database.OpenConnectionAsync(CancellationToken.None);

        // Act
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' ORDER BY name";
        var tables = new List<string>();

        await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
        {
            // WHY: The forward-only schema cursor is consumed once while retaining only names for collision checks.
            while (await reader.ReadAsync(CancellationToken.None))
            {
                tables.Add(reader.GetString(0));
            }
        }

        // Assert
        Assert.Contains("\u00c4Trees", tables);
        Assert.Contains("\u00e4Trees", tables);
        Assert.Contains("\u017fqlite_Trees", tables);
    }

    /// <summary>Still rejects an ASCII case variant on SQLite, which SQLite treats as the same table.</summary>
    [Fact]
    public void SqliteAsciiCaseVariantCollides()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Trees");
            Map<SecondNode>(modelBuilder, "Seconds", "TREES");
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.Contains("collides",
            Assert.IsType<InvalidOperationException>(error).Message);
    }

    /// <summary>Rejects a derived configuration that contradicts its base hierarchy's name.</summary>
    [Fact]
    public void ConflictingDerivedNameIsRejected()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<BaseNode>(modelBuilder, "Bases", "BaseTrees");
            modelBuilder
                .Entity<DerivedNode>()
                .HasBaseType<BaseNode>();
            Configure(modelBuilder.Entity<DerivedNode>(), "DerivedTrees");
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("base type", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Lets a derived type without its own configuration share the base registry.</summary>
    [Fact]
    public void DerivedTypeSharesTheBaseRegistry()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<BaseNode>(modelBuilder, "Bases", "BaseTrees");
            modelBuilder
                .Entity<DerivedNode>()
                .HasBaseType<BaseNode>();
        });

        // Act
        var mapping = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(DerivedNode))!);

        // Assert
        Assert.Equal("BaseTrees", mapping.Store.Name);
    }

    /// <summary>Lets a later call on the same hierarchy replace the name, as other options do.</summary>
    [Fact]
    public void LaterConfigurationReplacesTheName()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Earlier");
            modelBuilder
                .Entity<FirstNode>()
                .HasNestedSet(nestedSet => nestedSet.HasTreeRegistryTable("Later"));
        });

        // Act
        var registry = Registry<FirstNode>(context);

        // Assert
        Assert.Equal("Later", registry.GetTableName());
    }

    /// <summary>Finalizes a model at each provider's documented limit, measured in that provider's unit.</summary>
    /// <param name="provider">The provider under test.</param>
    /// <param name="name">A name exactly at that provider's limit.</param>
    [Theory]
    [MemberData(nameof(FittingNames))]
    public void NameAtTheProviderLimitIsAccepted(
        string provider,
        string name
    )
    {
        // Arrange
        using var context = CreateFor(provider, modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", name));

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;

        // Assert
        Assert.Equal(name,
            NestedSetTreeRegistryMapping.For(model.FindEntityType(typeof(FirstNode))!).Store.Name);
    }

    /// <summary>Rejects a name one unit beyond the limit and reports the measured length, unit and limit.</summary>
    /// <param name="provider">The provider under test.</param>
    /// <param name="name">A name one unit beyond that provider's limit.</param>
    /// <param name="measured">The expected measured length and unit.</param>
    /// <param name="limit">The expected limit statement.</param>
    [Theory]
    [MemberData(nameof(ExceedingNames))]
    public void NameBeyondTheProviderLimitIsRejected(
        string provider,
        string name,
        string measured,
        string limit
    )
    {
        // Arrange
        using var context = CreateFor(provider, modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", name));

        // Act
        var error = Record.Exception(() => _ = context.GetService<IDesignTimeModel>().Model);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(measured, failure.Message, StringComparison.Ordinal);
        Assert.Contains(limit, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Provides names at each limit, including multibyte names only PostgreSQL measures in bytes.</summary>
    public static TheoryData<string, string> FittingNames =>
        new()
        {
            { "SqlServer", new string('a', 128) },
            { "MySql", new string('a', 64) },
            { "MySql", new string('a', 63) + "\u00e4" },
            { "Postgres", new string('a', 63) },
            { "Postgres", new string('a', 61) + "\u00e4" },
        };

    /// <summary>Provides names one unit beyond each limit, with the expected diagnostic parts.</summary>
    public static TheoryData<string, string, string, string> ExceedingNames =>
        new()
        {
            { "SqlServer", new string('a', 129), "129 characters", "allows 128" },
            { "MySql", new string('a', 65), "65 characters", "allows 64" },
            { "Postgres", new string('a', 64), "64 bytes", "allows 63" },
            { "Postgres", new string('a', 62) + "\u00e4", "64 bytes", "allows 63" },
        };

    /// <summary>Rejects SQLite's reserved table prefix before attempting DDL.</summary>
    [Fact]
    public void SqliteReservedNameIsRejectedDuringFinalization()
    {
        // Arrange
        using var context = Create(modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", "SQLite_Trees"));

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.Contains("reserved",
            Assert.IsType<InvalidOperationException>(error).Message);
    }

    /// <summary>Rejects SQL Server's temporary-table prefix during design-time model finalization.</summary>
    [Fact]
    public void SqlServerReservedNameIsRejectedDuringFinalization()
    {
        // Arrange
        using var context = CreateSqlServer(modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", "#Trees"));

        // Act
        var error = Record.Exception(() => _ = context.GetService<IDesignTimeModel>().Model);

        // Assert
        Assert.Contains("reserved",
            Assert.IsType<InvalidOperationException>(error).Message);
    }

    /// <summary>Creates a SQLite context with its own service provider, and so its own uncached model.</summary>
    private static ConfiguredContext Create(
        Action<ModelBuilder> configure
    ) => CreateFor("Sqlite", configure);

    /// <summary>Creates a SQL Server context with its own service provider and uncached model.</summary>
    private static ConfiguredContext CreateSqlServer(
        Action<ModelBuilder> configure
    ) => CreateFor("SqlServer", configure);

    /// <summary>Creates a context for one provider with its own uncached model and no server connection.</summary>
    private static ConfiguredContext CreateFor(
        string provider,
        Action<ModelBuilder> configure
    )
    {
        var options = new DbContextOptionsBuilder<ConfiguredContext>()
            .ConfigureTestWarnings()
            .EnableServiceProviderCaching(false);

        _ = provider switch
        {
            "Sqlite" => options.UseSqlite("Data Source=:memory:", native => native.UseNestedSets()),
            "SqlServer" => options.UseSqlServer("Server=localhost;Database=unused", native => native.UseNestedSets()),
            "Postgres" => options.UseNpgsql("Host=localhost;Database=unused", native => native.UseNestedSets()),
            "MySql" => options.UseMySql(
                "Server=localhost;Database=unused",
                DatabaseTestTargets.MySql,
                native => native.UseNestedSets()),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };

        return new ConfiguredContext(options.Options, configure);
    }

    /// <summary>Finds the finalized registry of one hierarchy.</summary>
    private static IEntityType Registry<TNode>(
        DbContext context
    ) => NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(TNode))!).Registry;

    /// <summary>Maps one hierarchy with an optional explicit name and schema.</summary>
    private static void Map<TNode>(
        ModelBuilder modelBuilder,
        string table,
        string? registry,
        string? schema = null
    )
        where TNode : Node
    {
        var node = modelBuilder.Entity<TNode>();
        node.ToTable(table, schema);
        Configure(node, registry);
    }

    /// <summary>Selects the structural roles and the optional registry name.</summary>
    private static void Configure<TNode>(
        EntityTypeBuilder<TNode> node,
        string? registry
    )
        where TNode : Node => node.HasNestedSet(nestedSet =>
    {
        nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position);

        if (registry is not null)
        {
            nestedSet.HasTreeRegistryTable(registry);
        }
    });

    /// <summary>Applies a test-specific model configuration.</summary>
    private sealed class ConfiguredContext(
        DbContextOptions<ConfiguredContext> options,
        Action<ModelBuilder> configure
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => configure(modelBuilder);
    }

    /// <summary>Provides the mapped hierarchy shape.</summary>
    private abstract class Node
    {
        /// <summary>Gets or sets the stable identity.</summary>
        public long Id { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the direct parent identity.</summary>
        public long? ParentId { get; set; }

        /// <summary>Gets or sets the inclusive left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the inclusive right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the distance from the root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Provides a first hierarchy.</summary>
    private sealed class FirstNode : Node;

    /// <summary>Provides a second hierarchy.</summary>
    private sealed class SecondNode : Node;

    /// <summary>Provides a third hierarchy.</summary>
    private sealed class ThirdNode : Node;

    /// <summary>Provides a base hierarchy.</summary>
    private class BaseNode : Node;

    /// <summary>Provides a derived hierarchy type.</summary>
    private sealed class DerivedNode : BaseNode;

    /// <summary>Provides an application table.</summary>
    private sealed class Order
    {
        /// <summary>Gets or sets the identity.</summary>
        public long Id { get; set; }
    }
}
