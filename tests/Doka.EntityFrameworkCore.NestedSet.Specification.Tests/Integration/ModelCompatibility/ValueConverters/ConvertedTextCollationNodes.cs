namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Retains a reference model key whose provider representation is text.</summary>
internal sealed record ReferenceTextKey(string Value);

/// <summary>Retains a value model key whose nullable parent uses the same text conversion.</summary>
internal readonly record struct ValueTextKey(string Value);

/// <summary>Uses EF's built-in enum-to-string conversion for assigned node identities.</summary>
internal enum EnumTextKey
{
    Root,
    Child,
    Other,
    Leaf,
    Missing,
}

/// <summary>Shares structural roles while retaining each key and parent's exact model types.</summary>
internal sealed class ConvertedTextCollationNode<TKey, TParent>
{
    /// <summary>Gets or sets the assigned model key.</summary>
    internal TKey Id { get; set; } = default!;

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the nullable model parent key.</summary>
    internal TParent ParentId { get; set; } = default!;

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the configured sibling ordering payload.</summary>
    internal string Name { get; set; } = string.Empty;
}

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

/// <summary>Uses custom converters for nullable reference parents.</summary>
internal sealed class ReferenceTextCollationContext(DbContextOptions<ReferenceTextCollationContext> options)
    : ConvertedTextCollationContext<ReferenceTextKey, ReferenceTextKey?>(options)
{
    /// <inheritdoc />
    protected override string Table => "ReferenceTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<ReferenceTextKey, ReferenceTextKey?>> node
    )
    {
        node
            .Property(value => value.Id)
            .HasConversion(value => value.Value, value => new ReferenceTextKey(value));
        node
            .Property(value => value.ParentId)
            .HasConversion(value => value!.Value, value => new ReferenceTextKey(value));
    }
}

/// <summary>Uses custom converters for nullable value parents.</summary>
internal sealed class ValueTextCollationContext(DbContextOptions<ValueTextCollationContext> options)
    : ConvertedTextCollationContext<ValueTextKey, ValueTextKey?>(options)
{
    /// <inheritdoc />
    protected override string Table => "ValueTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<ValueTextKey, ValueTextKey?>> node
    )
    {
        node
            .Property(value => value.Id)
            .HasConversion(value => value.Value, value => new ValueTextKey(value));
        node
            .Property(value => value.ParentId)
            .HasConversion(
                value => value.HasValue ? value.Value.Value : null,
                value => value == null ? null : new ValueTextKey(value));
    }
}

/// <summary>Uses provider-type configuration rather than an explicitly supplied converter.</summary>
internal sealed class EnumTextCollationContext(DbContextOptions<EnumTextCollationContext> options)
    : ConvertedTextCollationContext<EnumTextKey, EnumTextKey?>(options)
{
    /// <inheritdoc />
    protected override string Table => "EnumTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<EnumTextKey, EnumTextKey?>> node
    )
    {
        node
            .Property(value => value.Id)
            .HasConversion<string>();
        node
            .Property(value => value.ParentId)
            .HasConversion<string>();
    }
}

/// <summary>Uses the same nullable model type for the required key and optional parent.</summary>
internal sealed class NullableEnumTextCollationContext(DbContextOptions<NullableEnumTextCollationContext> options)
    : ConvertedTextCollationContext<EnumTextKey?, EnumTextKey?>(options)
{
    /// <inheritdoc />
    protected override string Table => "NullableEnumTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>> node
    )
    {
        node
            .Property(value => value.Id)
            .HasConversion<string>();
        node
            .Property(value => value.ParentId)
            .HasConversion<string>();
    }
}

/// <summary>Lets the relational text store type select the effective enum converter.</summary>
internal sealed class ImplicitEnumTextCollationContext(DbContextOptions<ImplicitEnumTextCollationContext> options)
    : ConvertedTextCollationContext<EnumTextKey, EnumTextKey?>(options)
{
    /// <inheritdoc />
    protected override string Table => "ImplicitEnumTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<EnumTextKey, EnumTextKey?>> node
    )
    {
        var storeType = Database.IsSqlite()
            ? "TEXT"
            : Database.IsNpgsql()
                ? "character varying(64)"
                : "varchar(64)";

        node
            .Property(value => value.Id)
            .HasColumnType(storeType);
        node
            .Property(value => value.ParentId)
            .HasColumnType(storeType);
    }
}

/// <summary>Protects registry and runtime capture parity for provider-selected identity conversions.</summary>
internal sealed class ImplicitRegistryTextCollationContext(
    DbContextOptions<ImplicitRegistryTextCollationContext> options
) : ConvertedTextCollationContext<EnumTextKey, EnumTextKey?>(options)
{
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
