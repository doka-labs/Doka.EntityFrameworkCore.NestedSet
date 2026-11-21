namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps two entities to one table through an identifying one-to-one relationship.</summary>
internal sealed class TableSplitContext : DbContext
{
    /// <summary>Creates a table-splitting compatibility context.</summary>
    internal TableSplitContext(
        DbContextOptions<TableSplitContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<TableSplitNode>();
        node.ToTable("TableSplitNodes");
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));

        var payload = modelBuilder.Entity<TableSplitPayload>();
        payload.ToTable("TableSplitNodes");
        payload.HasKey(entity => entity.Id);
        payload.Property(entity => entity.Description);
        payload
            .HasOne<TableSplitNode>()
            .WithOne()
            .HasForeignKey<TableSplitPayload>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
