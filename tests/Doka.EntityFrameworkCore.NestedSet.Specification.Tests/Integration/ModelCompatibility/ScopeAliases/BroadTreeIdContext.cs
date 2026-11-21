namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps two case-distinct stored TreeIds whose CLR values compare equal.</summary>
internal sealed class BroadTreeIdContext : DbContext
{
    /// <summary>Creates a context for the model-compatibility database.</summary>
    internal BroadTreeIdContext(
        DbContextOptions<BroadTreeIdContext> options
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

        var node = modelBuilder.Entity<BroadTreeIdNode>();
        node.ToTable("BroadTreeIdNodes");
        node.HasKey(entity => entity.Id);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node
            .Property(entity => entity.Name)
            .HasMaxLength(40);

        var treeId = node
            .Property(entity => entity.TreeId)
            .HasConversion(value => value.Value, value => new BroadTreeId(value))
            .HasMaxLength(40);

        if (collation is not null)
        {
            treeId.UseCollation(collation);
        }

        // WHY: EF's registry key must distinguish the same stored TreeIds as its case-sensitive column.
        var exact = new ValueComparer<BroadTreeId>(
            (first,
                second) => StringComparer.Ordinal.Equals(first!.Value, second!.Value),
            value => StringComparer.Ordinal.GetHashCode(value.Value),
            value => new BroadTreeId(value.Value));

        treeId.Metadata.SetValueComparer(exact);
        node
            .HasOne<BroadTreeIdNode>()
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
            .OrderBy(entity => entity.Name));
    }
}
