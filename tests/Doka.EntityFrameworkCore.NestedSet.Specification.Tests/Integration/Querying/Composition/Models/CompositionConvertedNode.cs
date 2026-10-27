namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides an integer-backed identity without CLR equality or ordering operators.</summary>
internal readonly struct CompositionNodeKey : IEquatable<CompositionNodeKey>
{
    /// <summary>Creates a key with an explicitly converted provider representation.</summary>
    internal CompositionNodeKey(
        int value
    )
    {
        Value = value;
    }

    /// <summary>Gets the provider integer represented by this value object.</summary>
    internal int Value { get; }

    /// <inheritdoc />
    public bool Equals(
        CompositionNodeKey other
    ) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is CompositionNodeKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value;
}

/// <summary>Uses converted identities and a nullable converted parent within an independently scoped tree.</summary>
internal sealed class CompositionConvertedNode
{
    /// <summary>Gets or sets the scope component of the relational identity.</summary>
    internal Guid Scope { get; set; }

    /// <summary>Gets or sets the converted node key, including the valid zero value.</summary>
    internal CompositionNodeKey Id { get; set; }

    /// <summary>Gets or sets the converted nullable parent key.</summary>
    internal CompositionNodeKey? ParentId { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the stored structural depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    internal long Position { get; set; }
}

/// <summary>Maps an operator-free value object consistently on its principal and nullable parent keys.</summary>
internal sealed class CompositionConvertedContext : DbContext
{
    /// <summary>Creates the converted-key context using the actual provider fixture.</summary>
    internal CompositionConvertedContext(
        DbContextOptions<CompositionConvertedContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<CompositionConvertedNode>();
        node.ToTable("CompositionConvertedNodes");
        node.Property(entity => entity.Id)
            .HasConversion(key => key.Value, value => new CompositionNodeKey(value))
            .ValueGeneratedNever();
        node.Property(entity => entity.ParentId).HasConversion(
            key => key.HasValue ? key.Value.Value : (int?)null,
            value => value.HasValue ? new CompositionNodeKey(value.Value) : null);
        node.HasKey(entity => new { entity.Scope, entity.Id });
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(entity => entity.Id)
            .HasScope(entity => entity.Scope)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }
}
