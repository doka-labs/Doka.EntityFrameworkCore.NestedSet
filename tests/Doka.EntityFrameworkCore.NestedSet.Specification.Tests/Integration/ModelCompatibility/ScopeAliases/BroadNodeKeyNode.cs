namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Has broader domain equality than its case-sensitive stored node identity.</summary>
internal sealed class BroadNodeKey(string value) : IEquatable<BroadNodeKey>
{
    /// <summary>Gets the identity written by the value converter.</summary>
    internal string Value { get; } = value;

    /// <inheritdoc />
    public bool Equals(
        BroadNodeKey? other
    ) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is BroadNodeKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}

/// <summary>Represents a hierarchy with converted node and parent keys.</summary>
internal sealed class BroadNodeKeyNode
{
    /// <summary>Gets or sets the node identity.</summary>
    internal BroadNodeKey Id { get; set; } = null!;

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the parent identity.</summary>
    internal BroadNodeKey? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the sibling sort value when ordering is configured.</summary>
    internal string Name { get; set; } = "";
}

/// <summary>Maps domain-equal key aliases to distinct database identities.</summary>
internal sealed class BroadNodeKeyContext : DbContext
{
    /// <summary>Creates a context for the model-compatibility database.</summary>
    internal BroadNodeKeyContext(
        DbContextOptions<BroadNodeKeyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => BroadNodeKeyModel.Configure(modelBuilder, this, "BroadNodeKeyNodes", ordered: false);
}

/// <summary>Maps the same converted keys through automatic sibling ordering.</summary>
internal sealed class OrderedBroadNodeKeyContext : DbContext
{
    /// <summary>Creates a sorted context for the model-compatibility database.</summary>
    internal OrderedBroadNodeKeyContext(
        DbContextOptions<OrderedBroadNodeKeyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => BroadNodeKeyModel.Configure(modelBuilder, this, "OrderedBroadNodeKeyNodes", ordered: true);
}

/// <summary>Shares mapped key and parent semantics between manual and sorted imports.</summary>
internal static class BroadNodeKeyModel
{
    /// <summary>Configures one independently named hierarchy table and its chosen ordering mode.</summary>
    internal static void Configure(
        ModelBuilder modelBuilder,
        DbContext context,
        string table,
        bool ordered
    )
    {
        if (ordered && context.Database.IsNpgsql())
        {
            // WHY: PostgreSQL needs an explicit nondeterministic ICU collation to exercise database-equal
            // spellings while the imported provider representations still differ.
            modelBuilder.HasCollation(
                "broad_node_key_ci",
                locale: "und-u-ks-level2",
                provider: "icu",
                deterministic: false);
        }

        var collation = context.Database.IsSqlite()
            ? ordered ? "NOCASE" : "BINARY"
            : context.Database.IsSqlServer()
                ? ordered ? "Latin1_General_100_CI_AS" : "Latin1_General_100_BIN2"
                : context.Database.IsNpgsql()
                    ? ordered ? "broad_node_key_ci" : "C"
                    : ordered
                        ? "utf8mb4_general_ci"
                        : "utf8mb4_bin";

        var node = modelBuilder.Entity<BroadNodeKeyNode>();
        node.ToTable(table);
        node.HasKey(entity => entity.Id);
        var key = node
            .Property(entity => entity.Id)
            .HasConversion(value => value.Value, value => new BroadNodeKey(value))
            .HasMaxLength(40)
            .UseCollation(collation)
            .ValueGeneratedNever();

        var parent = node
            .Property(entity => entity.ParentId)
            .HasConversion(
                value => value == null ? null : value.Value,
                value => value == null ? null : new BroadNodeKey(value))
            .HasMaxLength(40)
            .UseCollation(collation);
        node
            .Property(entity => entity.Name)
            .HasMaxLength(40);

        // WHY: EF relationship fixup follows each database collation, while the sorted refresh must still
        // reject changes to the actual stored representation even when the database equates the spellings.
        var comparer = new ValueComparer<BroadNodeKey>(
            (
                first,
                second
            ) => ordered
                ? StringComparer.OrdinalIgnoreCase.Equals(first!.Value, second!.Value)
                : StringComparer.Ordinal.Equals(first!.Value, second!.Value),
            value => ordered
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(value.Value)
                : StringComparer.Ordinal.GetHashCode(value.Value),
            value => new BroadNodeKey(value.Value));

        key.Metadata.SetValueComparer(comparer);
        parent.Metadata.SetValueComparer(comparer);
        node
            .HasOne<BroadNodeKeyNode>()
            .WithMany()
            .HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        node.HasNestedSet(nestedSet =>
        {
            nestedSet
                .HasNodeKey(entity => entity.Id)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position);

            if (ordered)
            {
                nestedSet.OrderBy(entity => entity.Name);
            }
        });
    }
}
