namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps a text NodeKey that needs ordinal rowsets and repeats across tenants.</summary>
internal sealed class CompositeTextKeyContext : DbContext
{
    /// <summary>Creates a tenant-local text key compatibility context.</summary>
    internal CompositeTextKeyContext(
        DbContextOptions<CompositeTextKeyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<CompositeTextKeyNode>();
        node.HasKey(entity => new
        {
            entity.TenantId,
            entity.NodeKey,
        });

        node
            .Property(entity => entity.NodeKey)
            .HasMaxLength(40)
            .ValueGeneratedNever();

        node
            .Property(entity => entity.ParentNodeKey)
            .HasMaxLength(40);

        node
            .Property(entity => entity.Name)
            .HasMaxLength(100);

        node
            .HasOne<CompositeTextKeyNode>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.TenantId,
                entity.ParentNodeKey,
            })
            .OnDelete(DeleteBehavior.Restrict);

        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.NodeKey)
            .HasScope(entity => entity.TenantId)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentNodeKey)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position)
            .OrderBy(entity => entity.Name));
    }
}
