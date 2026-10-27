namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Reuses the existing binary-key entity with isolated composition tables.</summary>
internal sealed class CompositionBinaryContext : DbContext
{
    /// <summary>Creates the binary-key context using the actual provider fixture.</summary>
    internal CompositionBinaryContext(
        DbContextOptions<CompositionBinaryContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<BinaryNode>();
        node.ToTable("CompositionBinaryNodes");
        node.Property(entity => entity.Id).HasMaxLength(16).ValueGeneratedNever();
        node.Property(entity => entity.ParentId).HasMaxLength(16);
        node.HasNestedSet(mapping => mapping
            .HasScope(entity => entity.Tree)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId));
    }
}
