namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Has broader domain equality than its case-sensitive stored tree identity.</summary>
internal sealed class BroadTreeId(string value) : IEquatable<BroadTreeId>
{
    /// <summary>Gets the identity written by the value converter.</summary>
    internal string Value { get; } = value;

    /// <inheritdoc />
    public bool Equals(
        BroadTreeId? other
    ) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is BroadTreeId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}

/// <summary>Represents a hierarchy node with a converted reference-type TreeId.</summary>
internal sealed class BroadTreeIdNode
{
    /// <summary>Gets or sets the node key.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal BroadTreeId TreeId { get; set; } = null!;

    /// <summary>Gets or sets the parent key.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the configured sibling ordering value.</summary>
    internal string Name { get; set; } = "";
}

/// <summary>Maps two case-distinct stored TreeIds whose CLR values compare equal.</summary>
internal sealed class BroadTreeIdContext : DbContext
{
    /// <summary>Creates a context for the model-compatibility database.</summary>
    internal BroadTreeIdContext(
        DbContextOptions<BroadTreeIdContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var collation = Database.IsSqlite()
            ? "BINARY"
            : Database.IsSqlServer()
                ? "Latin1_General_100_BIN2"
                : Database.IsNpgsql()
                    ? null
                    : "utf8mb4_bin";

        var node = modelBuilder.Entity<BroadTreeIdNode>();
        node.ToTable("BroadTreeIdNodes");
        node.HasKey(entity => entity.Id);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();
        node
            .Property(entity => entity.Name)
            .HasMaxLength(40);
        var treeId = node
            .Property(entity => entity.TreeId)
            .HasConversion(value => value.Value, value => new BroadTreeId(value))
            .HasMaxLength(40);

        if (collation is not null)
        {
            treeId.UseCollation(collation);
        }

        // WHY: EF's registry key must distinguish the same stored TreeIds as its case-sensitive column.
        var exact = new ValueComparer<BroadTreeId>(
            (
                first,
                second
            ) => StringComparer.Ordinal.Equals(first!.Value, second!.Value),
            value => StringComparer.Ordinal.GetHashCode(value.Value),
            value => new BroadTreeId(value.Value));

        treeId.Metadata.SetValueComparer(exact);
        node
            .HasOne<BroadTreeIdNode>()
            .WithMany()
            .HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position)
            .OrderBy(entity => entity.Name));
    }
}
