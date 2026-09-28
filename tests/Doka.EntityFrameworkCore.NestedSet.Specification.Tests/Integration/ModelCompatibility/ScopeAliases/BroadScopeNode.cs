namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses broad domain equality while retaining its exact stored scope representation.</summary>
internal sealed class BroadScope(string value) : IEquatable<BroadScope>
{
    /// <summary>Gets the scope value stored by the database.</summary>
    internal string Value { get; } = value;

    /// <inheritdoc />
    public bool Equals(
        BroadScope? other
    ) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is BroadScope other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}

/// <summary>Represents one hierarchy node with a converted reference-type scope.</summary>
internal sealed class BroadScopeNode
{
    /// <summary>Gets or sets the forest scope.</summary>
    internal BroadScope Scope { get; set; } = null!;

    /// <summary>Gets or sets the node key within its scope.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

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

/// <summary>Maps CLR-broad scope equality onto case-sensitive provider identities.</summary>
internal class BroadScopeContext : DbContext
{
    /// <summary>Creates a context for the model-compatibility database.</summary>
    internal BroadScopeContext(
        DbContextOptions options
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

        var node = modelBuilder.Entity<BroadScopeNode>();
        node.ToTable("BroadScopeNodes");
        node.HasKey(entity => new
        {
            entity.Scope,
            entity.Id,
        });

        var scope = node
            .Property(entity => entity.Scope)
            .HasConversion(value => value.Value, value => new BroadScope(value))
            .HasMaxLength(40);

        if (collation is not null)
        {
            scope.UseCollation(collation);
        }

        // WHY: EF's identity map must agree with the distinct provider values, despite the domain equality.
        var exact = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<BroadScope>(
            (first, second) => StringComparer.Ordinal.Equals(first!.Value, second!.Value),
            value => StringComparer.Ordinal.GetHashCode(value.Value),
            value => new BroadScope(value.Value));

        scope.Metadata.SetValueComparer(exact);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();
        node
            .Property(entity => entity.Name)
            .HasMaxLength(40);
        node
            .HasOne<BroadScopeNode>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.Scope,
                entity.ParentId,
            })
            .OnDelete(DeleteBehavior.Restrict);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasScope(entity => entity.Scope)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position)
            .OrderBy(entity => entity.Name));
    }
}
