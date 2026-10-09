namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class IndexConventionTests
{
    /// <summary>A unique scope/key index covers maintenance lookups without constraining mutable coordinates.</summary>
    /// <param name="afterMapping">Whether the application index is added after the structural mapping.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UniqueMaintenanceIndexIsReusedAndAcceptedByService(
        bool afterMapping
    )
    {
        // Arrange
        IMutableIndex? existing = null;
        using var context = new ConventionContext("Sqlite", model =>
        {
            var entity = model.Entity<IndexNode>();
            entity.HasKey(node => node.Key);

            if (afterMapping)
            {
                Configure(entity);
            }

            existing = entity.HasIndex(node => new { node.Scope, node.Key }, "UserMaintenance")
                .IsUnique().Metadata;

            if (!afterMapping)
            {
                Configure(entity);
            }

        });

        // Act
        var service = context.NestedSet<IndexNode>().ForScope(1);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IndexNode))!;

        // Assert
        var maintenance = Assert.Single(entity.GetIndexes(), index =>
            index.Properties[1].Name == nameof(IndexNode.Key));

        Assert.Same(existing, maintenance);
        Assert.True(maintenance.IsUnique);
        Assert.Equal(5, entity.GetIndexes().Count());
        Assert.NotNull(service.InTree(Guid.Empty).Nodes);
    }

    /// <summary>
    /// Ignored required annotated properties fail during model finalization before any mutation begins.
    /// </summary>
    /// <param name="property">The previously selected structural property removed from the final EF model.</param>
    [Theory]
    [InlineData(nameof(IndexNode.Left))]
    [InlineData(nameof(IndexNode.Right))]
    [InlineData(nameof(IndexNode.Parent))]
    [InlineData(nameof(IndexNode.Position))]
    [InlineData(nameof(IndexNode.Depth))]
    [InlineData(nameof(IndexNode.TreeId))]
    [InlineData(nameof(IndexNode.Key))]
    public void IgnoredStructuralPropertyIsRejectedAtFinalization(
        string property
    )
    {
        // Arrange
        using var context = new ConventionContext("Sqlite", model =>
        {
            var entity = model.Entity<IndexNode>();
            entity.HasKey(node => node.Key);
            Configure(entity);

            if (property == nameof(IndexNode.Key))
            {
                entity.HasNestedSet(builder => builder.HasNodeKey(node => node.Key));
                entity.HasKey(node => node.OtherKey);
            }

            entity.Ignore(property);
        });

        // Act
        var exception = Record.Exception(() => context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("nested-set", invalid.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>PostgreSQL options that preserve the complete B-tree access path avoid duplicates.</summary>
    /// <param name="facet">The public Npgsql index option or explicit unfiltered setting to retain.</param>
    /// <param name="afterMapping">Whether the user adds the index after automatic structural configuration.</param>
    [Theory]
    [InlineData("btree", false)]
    [InlineData("include", false)]
    [InlineData("concurrent", false)]
    [InlineData("fillfactor", false)]
    [InlineData("unfiltered", false)]
    [InlineData("btree", true)]
    [InlineData("include", true)]
    [InlineData("concurrent", true)]
    [InlineData("fillfactor", true)]
    [InlineData("unfiltered", true)]
    public void EquivalentPostgreSqlIndexIsReused(
        string facet,
        bool afterMapping
    )
    {
        // Arrange
        using var context = new ConventionContext("PostgreSql", model =>
        {
            var entity = model.Entity<IndexNode>();
            entity.HasKey(node => node.Key);

            if (afterMapping)
            {
                Configure(entity);
            }

            var index = entity.HasIndex(node => new { node.Scope, node.TreeId, node.Left }, "UsefulLeft");
            ConfigurePostgreSql(index, facet);

            if (!afterMapping)
            {
                Configure(entity);
            }

        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IndexNode))!;

        // Assert
        var left = Assert.Single(entity.GetIndexes(), index => index.Properties.Count > 2
            && index.Properties[2].Name == nameof(IndexNode.Left));

        Assert.Equal("UsefulLeft", left.Name);
        Assert.Null(left.GetFilter());
        Assert.Equal(5, entity.GetIndexes().Count());
    }

    /// <summary>Convention-generated names do not make a replaceable ordinary index application-owned.</summary>
    [Fact]
    public void ConventionDatabaseNameDoesNotPreventDeduplication()
    {
        // Arrange
        using var context = new ConventionContext("Sqlite", model =>
        {
            var entity = model.Entity<IndexNode>();
            entity.HasKey(node => node.Key);
            Configure(entity);
            var owned = entity.Metadata.GetIndexes()
                .Single(index => index.Properties[2].Name == nameof(IndexNode.Left));

            ((IConventionIndex)owned).SetDatabaseName("ConventionLeft");
            entity.HasIndex(node => new { node.Scope, node.TreeId, node.Left }, "UserLeft");
        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IndexNode))!;

        // Assert
        var left = Assert.Single(entity.GetIndexes(), index => index.Properties.Count > 2
            && index.Properties[2].Name == nameof(IndexNode.Left));

        Assert.Equal("UserLeft", left.Name);
        Assert.Equal(4, entity.GetIndexes().Count());
    }

    /// <summary>Identical CLR entities in distinct tables cannot collide through fallback index names.</summary>
    /// <param name="engine">The provider whose index names share a schema or database namespace.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public void FallbackDatabaseNameUsesFinalPhysicalTable(
        string engine
    )
    {
        // Arrange
        using var first = new ConventionContext(engine, model => ConfigureSpecializedTable(model, "FirstFolders"));
        using var second = new ConventionContext(engine, model => ConfigureSpecializedTable(model, "SecondFolders"));

        // Act
        var firstName = FallbackName(first);
        var secondName = FallbackName(second);

        // Assert
        Assert.NotEqual(firstName, secondName);
    }

    /// <summary>Column renaming participates in fallback identity regardless of configuration order.</summary>
    /// <param name="beforeMapping">Whether relational names are applied before structural mapping.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackDatabaseNameUsesFinalPhysicalColumns(
        bool beforeMapping
    )
    {
        // Arrange
        using var baseline = new ConventionContext("Sqlite", model => ConfigureSpecializedTable(model, "Folders"));
        using var renamed = new ConventionContext("Sqlite", model =>
        {
            var entity = model.Entity<IndexNode>();
            if (beforeMapping)
            {
                entity.Property(node => node.Left).HasColumnName("LeftBoundary");
            }

            ConfigureSpecializedTable(model, "Folders");

            if (!beforeMapping)
            {
                entity.Property(node => node.Left).HasColumnName("LeftBoundary");
            }

        });

        // Act
        var originalName = FallbackName(baseline);
        var renamedName = FallbackName(renamed);

        // Assert
        Assert.NotEqual(originalName, renamedName);
    }

    /// <summary>Exercises public Npgsql builders to verify the serialized annotation names and values.</summary>
    private static void ConfigurePostgreSql(
        IndexBuilder<IndexNode> index,
        string facet
    )
    {
        switch (facet)
        {
            case "btree":
                index.HasMethod("btree");
                break;
            case "include":
                NpgsqlIndexBuilderExtensions.IncludeProperties(index, node => node.OtherKey);
                break;
            case "concurrent":
                index.IsCreatedConcurrently();
                break;
            case "fillfactor":
                index.HasStorageParameter("fillfactor", 70);
                break;
            case "unfiltered":
                index.HasFilter(null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(facet));
        }

    }

    /// <summary>Maps the same CLR entity to a later-selected table with a specialized existing index.</summary>
    private static void ConfigureSpecializedTable(
        ModelBuilder model,
        string table
    )
    {
        var entity = model.Entity<IndexNode>();
        entity.HasKey(node => node.Key);
        entity.HasIndex(node => new { node.Scope, node.TreeId, node.Left }).HasFilter("\"Left\" > 0");
        Configure(entity);
        entity.ToTable(table);
    }

    /// <summary>Gets the additional ordinary access path's physical name from the finalized design model.</summary>
    private static string FallbackName(
        ConventionContext context
    ) => context
        .GetService<IDesignTimeModel>()
        .Model
        .FindEntityType(typeof(IndexNode))!
        .GetIndexes()
        .Single(index =>
            index.Name is not null
            && index.Properties.Contains(index.DeclaringEntityType.FindProperty(nameof(IndexNode.TreeId))!))
        .GetDatabaseName()!;
}
