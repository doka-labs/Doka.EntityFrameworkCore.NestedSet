namespace Doka.EntityFrameworkCore.NestedSet.Tests;

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
