namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies scoped registry mappings without application-owned lock entities.</summary>
public sealed class EnterpriseMappingTests
{
    /// <summary>A scoped hierarchy needs no tenant, workspace, or dedicated anchor entity.</summary>
    [Theory]
    [InlineData("string-default")]
    [InlineData("string-matching")]
    public void ScopeMappingCreatesTypedRegistryWithoutAnchor(
        string variant
    )
    {
        // Arrange
        using var context = CreateContext(variant);

        // Act
        var service = context
            .NestedSet<TextNode>()
            .ForScope("tenant");

        var registry = context
            .Model
            .GetEntityTypes()
            .Single(entity => entity.FindAnnotation(NestedSetAnnotationNames.TreeRegistryOwner) is not null);

        // Assert
        Assert.NotNull(service);
        Assert.NotNull(registry.FindProperty(NestedSetTreeRegistryMetadata.Scope));
        Assert.NotNull(registry.FindProperty(NestedSetTreeRegistryMetadata.TreeId));
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Builds one variant-aware model without opening a database connection.</summary>
    private static ScopeMappingContext CreateContext(
        string variant
    )
    {
        var options = new DbContextOptionsBuilder<ScopeMappingContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .Options;

        return new ScopeMappingContext(options, variant);
    }
}

/// <summary>Constructs variant-aware string Scope mappings for registry contract verification.</summary>
public sealed class ScopeMappingContext : DbContext, ITestModelVariant
{
    /// <summary>Creates an isolated mapping variant.</summary>
    /// <param name="options">The SQLite options and variant-aware model cache.</param>
    /// <param name="variant">The mapping shape to construct.</param>
    public ScopeMappingContext(
        DbContextOptions<ScopeMappingContext> options,
        string variant
    ) : base(options)
    {
        Variant = variant;
    }

    /// <summary>Gets the shape included in the model cache key.</summary>
    public string Variant { get; }

    /// <inheritdoc />
    object ITestModelVariant.ModelVariant => Variant;

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<TextNode>();
        node
            .Property(row => row.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();
        node
            .Property(row => row.Tree)
            .HasMaxLength(64);
        node
            .Property(row => row.ParentId)
            .HasMaxLength(64);

        if (Variant == "string-matching")
        {
            node
                .Property(row => row.Tree)
                .UseCollation("NOCASE");
        }

        node.HasNestedSet(builder => builder
            .HasScope(row => row.Tree)
            .HasTreeId(row => row.TreeId)
            .HasParent(row => row.ParentId));
    }
}
