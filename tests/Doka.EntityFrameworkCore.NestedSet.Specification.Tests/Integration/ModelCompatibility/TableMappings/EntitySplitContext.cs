namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps every structural property to one secondary entity-splitting fragment.</summary>
internal sealed class EntitySplitContext : DbContext
{
    /// <summary>Creates an entity-splitting compatibility context.</summary>
    internal EntitySplitContext(
        DbContextOptions<EntitySplitContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<EntitySplitNode>();
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node.ToTable("EntitySplitPayload", fragment => fragment.Property(entity => entity.Name));
        node.SplitToTable(
            "EntitySplitStructure",
            fragment =>
            {
                fragment.Property(entity => entity.TreeId);
                fragment.Property(entity => entity.ParentId);
                fragment.Property(entity => entity.Left);
                fragment.Property(entity => entity.Right);
                fragment.Property(entity => entity.Depth);
                fragment.Property(entity => entity.Position);
            });

        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }
}
