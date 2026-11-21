namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies explicit tree-registry table names, their diagnostics and the unchanged default.</summary>
public sealed partial class TreeRegistryTableNameTests
{
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

    /// <summary>Keeps the exact generated name when no explicit name is configured.</summary>
    [Fact]
    public void Registry_DefaultName_IsUnchangedWithoutTheOption()
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
    public void Registry_ExplicitName_AppliesToItsHierarchyOnly()
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
    public void Registry_ExplicitName_StaysInTheHierarchySchema()
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
    public void FinalizeModel_InvalidNames_AreRejectedAtTheBuilder(
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
    public void HasNestedSet_FailedCallback_LeavesNoAnnotation()
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

    /// <summary>Rejects a derived configuration that contradicts its base hierarchy's name.</summary>
    [Fact]
    public void FinalizeModel_ConflictingDerivedName_IsRejected()
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
    public void For_DerivedType_SharesTheBaseRegistry()
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
    public void Registry_LaterConfiguration_ReplacesTheName()
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
    ) => NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(TNode))!)
        .Registry;

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
}
