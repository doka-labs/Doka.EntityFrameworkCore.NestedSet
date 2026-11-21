namespace Doka.EntityFrameworkCore.NestedSet.Tests;

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
        node
            .Property(entity => entity.Id)
            .HasConversion(key => key.Value, value => new CompositionNodeKey(value))
            .ValueGeneratedNever();

        node
            .Property(entity => entity.ParentId)
            .HasConversion(
                key => key.HasValue
                    ? key.Value.Value
                    : (int?)null,
                value => value.HasValue
                    ? new CompositionNodeKey(value.Value)
                    : null);

        node.HasKey(entity => new
        {
            entity.Scope,
            entity.Id,
        });

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
