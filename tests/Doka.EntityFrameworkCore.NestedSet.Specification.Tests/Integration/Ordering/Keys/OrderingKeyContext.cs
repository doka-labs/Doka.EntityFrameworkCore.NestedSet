namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps native and converted ordered identities through ordinary asynchronous save integration.</summary>
public sealed class OrderingKeyContext : NestedSetDbContext
{
    /// <summary>Names the fixture-owned PostgreSQL collation used for case-insensitive key aliases.</summary>
    private const string PostgreSqlKeyCollation = "ordering_key_case_insensitive";

    /// <summary>Creates a context using its fixture's selected relational provider.</summary>
    /// <param name="options">The provider and connection options.</param>
    public OrderingKeyContext(
        DbContextOptions<OrderingKeyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var text = ConfigureNode<string, string?>(modelBuilder, "OrderingKeyTextNodes");
        ConfigureNode<Guid, Guid?>(modelBuilder, "OrderingKeyGuidNodes");
        ConfigureNode<byte[], byte[]?>(modelBuilder, "OrderingKeyBinaryNodes");
        var converted = ConfigureNode<int, int?>(modelBuilder, "OrderingKeyConvertedNodes");
        converted
            .Property(node => node.Id)
            .HasConversion<string>()
            .HasMaxLength(64);

        converted
            .Property(node => node.ParentId)
            .HasConversion<string>()
            .HasMaxLength(64);

        if (Database.IsNpgsql())
        {
            // WHY: PostgreSQL requires a nondeterministic ICU collation for case-insensitive identity equality.
            modelBuilder.HasCollation(
                PostgreSqlKeyCollation,
                locale: "und-u-ks-level2",
                provider: "icu",
                deterministic: false);
        }

        var keyCollation = Database.IsSqlite()
            ? "NOCASE"
            : Database.IsNpgsql()
                ? PostgreSqlKeyCollation
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

        text
            .Property(node => node.Id)
            .UseCollation(keyCollation);

        // WHY: A different parent collation exposes lookups that incorrectly compare aliases using the FK column.
        text
            .Property(node => node.ParentId)
            .UseCollation(parentCollation);
    }

    /// <summary>Maps one standalone hierarchy before applying any key-specific storage facets.</summary>
    private static EntityTypeBuilder<OrderingKeyNode<TKey, TParent>> ConfigureNode<TKey, TParent>(
        ModelBuilder modelBuilder,
        string table
    )
        where TKey : notnull
    {
        var node = modelBuilder.Entity<OrderingKeyNode<TKey, TParent>>();
        node.ToTable(table);
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();

        node
            .Property(value => value.ParentId)
            .IsRequired(false);

        node
            .Property(value => value.Name)
            .HasMaxLength(128);

        if (typeof(TKey) == typeof(string)
            || typeof(TKey) == typeof(byte[]))
        {
            node
                .Property(value => value.Id)
                .HasMaxLength(64);

            node
                .Property(value => value.ParentId)
                .HasMaxLength(64);
        }

        node.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId)
            .OrderBy(value => value.Name));

        return node;
    }
}
