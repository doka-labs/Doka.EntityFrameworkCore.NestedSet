namespace Doka.EntityFrameworkCore.NestedSet.Tests;

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
                id => id.HasValue
                    ? id.Value.Value
                    : (int?)null,
                value => value.HasValue
                    ? new StrongNodeId(value.Value)
                    : null);

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
