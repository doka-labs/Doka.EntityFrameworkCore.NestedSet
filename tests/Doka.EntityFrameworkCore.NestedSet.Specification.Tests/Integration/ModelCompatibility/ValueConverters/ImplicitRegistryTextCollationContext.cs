namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects registry and runtime capture parity for provider-selected identity conversions.</summary>
internal sealed class ImplicitRegistryTextCollationContext : ConvertedTextCollationContext<EnumTextKey, EnumTextKey?>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public ImplicitRegistryTextCollationContext(
        DbContextOptions<ImplicitRegistryTextCollationContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string Table => "ImplicitRegistryTextCollation";

    /// <summary>Gets the provider's relational text store type without requiring a configured converter.</summary>
    private string TextStore =>
        Database.IsSqlite()
            ? "TEXT"
            : Database.IsNpgsql()
                ? "character varying(64)"
                : "varchar(64)";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<EnumTextKey, EnumTextKey?>> node
    )
    {
        node
            .Property(value => value.Id)
            .HasColumnType(TextStore);

        node
            .Property(value => value.ParentId)
            .HasColumnType(TextStore);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        var node = modelBuilder.Entity<ConvertedTextCollationNode<EnumTextKey, EnumTextKey?>>();
        var collation = Database.IsSqlite()
            ? "NOCASE"
            : Database.IsNpgsql()
                ? Table.ToLowerInvariant() + "_ci"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_CI_AS"
                    : "utf8mb4_general_ci";

        foreach (var name in new[] { "Scope", "NativeTree" })
        {
            node
                .Property<EnumTextKey>(name)
                .HasColumnType(TextStore)
                .HasMaxLength(64)
                .UseCollation(collation);
        }

        node.HasNestedSet(nestedSet => nestedSet
            .HasScope("Scope")
            .HasTreeId("NativeTree"));
    }
}
