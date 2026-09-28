namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents an ordered hierarchy whose converted NodeKey has no provider collection transport.</summary>
internal sealed class OrderedStrongIdNode
{
    /// <summary>Gets or sets the strongly typed node identity.</summary>
    internal StrongNodeId Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the strongly typed direct parent identity.</summary>
    internal StrongNodeId? ParentId { get; set; }

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

/// <summary>Maps converted NodeKey and Parent values with one configurable placement policy.</summary>
internal abstract class OrderedStrongIdContext : DbContext
{
    /// <summary>Creates a context for the placement policy selected by the derived type.</summary>
    protected OrderedStrongIdContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the placement policy.</summary>
    protected abstract NestedSetOrderMode Mode { get; }

    /// <summary>Gets the independent table, keeping derived registry and index names short.</summary>
    protected abstract string Table { get; }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<OrderedStrongIdNode>();
        node.ToTable(Table);
        node
            .Property(entity => entity.Id)
            .HasConversion(id => id.Value, value => new StrongNodeId(value))
            .ValueGeneratedNever();
        node
            .Property(entity => entity.ParentId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (int?)null,
                value => value.HasValue ? new StrongNodeId(value.Value) : null);
        node
            .Property(entity => entity.Name)
            .HasMaxLength(32);
        node
            .HasOne<OrderedStrongIdNode>()
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
            .OrderBy(entity => entity.Name)
            .HasOrderMode(Mode));
    }
}

/// <summary>Uses strict sibling ordering for converted keys.</summary>
internal sealed class StrictStrongIdContext : OrderedStrongIdContext
{
    /// <summary>Creates the strict converted-key context.</summary>
    internal StrictStrongIdContext(
        DbContextOptions<StrictStrongIdContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override NestedSetOrderMode Mode => NestedSetOrderMode.Strict;

    /// <inheritdoc />
    protected override string Table => "StrictStrongIdNodes";
}

/// <summary>Allows manual placement, so changed nodes are placed one at a time.</summary>
internal sealed class PlacedStrongIdContext : OrderedStrongIdContext
{
    /// <summary>Creates the manual-placement converted-key context.</summary>
    internal PlacedStrongIdContext(
        DbContextOptions<PlacedStrongIdContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override NestedSetOrderMode Mode => NestedSetOrderMode.AllowManualPlacement;

    /// <inheritdoc />
    protected override string Table => "PlacedStrongIdNodes";
}
