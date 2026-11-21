namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps case-insensitive principal text and case-sensitive parent text without erasing model types.</summary>
internal abstract class ConvertedTextCollationContext<TKey, TParent> : DbContext
{
    /// <summary>Creates a context for one exact pair of model types.</summary>
    protected ConvertedTextCollationContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the independent table for this converter shape.</summary>
    protected abstract string Table { get; }

    /// <summary>Configures the exact key and nullable parent converters.</summary>
    protected abstract void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<TKey, TParent>> node
    );

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var postgresCollation = Table.ToLowerInvariant() + "_ci";

        if (Database.IsNpgsql())
        {
            modelBuilder.HasCollation(
                postgresCollation,
                locale: "und-u-ks-level2",
                provider: "icu",
                deterministic: false);
        }

        var principalCollation = Database.IsSqlite()
            ? "NOCASE"
            : Database.IsNpgsql()
                ? postgresCollation
                : Database.IsSqlServer()
                    ? "Latin1_General_100_CI_AS"
                    : "utf8mb4_general_ci";

        var parentCollation = Database.IsSqlite()
            ? "BINARY"
            : Database.IsNpgsql()
                ? "C"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_BIN2"
                    : "utf8mb4_bin";

        var node = modelBuilder.Entity<ConvertedTextCollationNode<TKey, TParent>>();
        node.ToTable(Table);
        ConvertKeys(node);
        node
            .Property(value => value.Id)
            .IsRequired()
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .UseCollation(principalCollation);

        node
            .Property(value => value.ParentId)
            .HasMaxLength(64)
            .UseCollation(parentCollation);

        node
            .Property(value => value.Name)
            .HasMaxLength(64);

        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(value => value.Id)
            .HasTreeId(value => value.TreeId)
            .HasParent(value => value.ParentId)
            .HasBounds(value => value.Left, value => value.Right)
            .HasDepth(value => value.Depth)
            .HasPosition(value => value.Position)
            .OrderBy(value => value.Name));
    }
}
