namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Maps strong NodeKey and Parent values to compatible relational integer columns.</summary>
internal sealed class StrongIdContext : DbContext
{
    /// <summary>Creates a value-converter compatibility context.</summary>
    internal StrongIdContext(
        DbContextOptions<StrongIdContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<StrongIdNode>();
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
            .HasOne<StrongIdNode>()
            .WithMany()
            .HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }
}
