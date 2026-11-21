namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares mapped key and parent semantics between manual and sorted imports.</summary>
internal static class BroadNodeKeyModel
{
    /// <summary>Configures one independently named hierarchy table and its chosen ordering mode.</summary>
    internal static void Configure(
        ModelBuilder modelBuilder,
        DbContext context,
        string table,
        bool ordered
    )
    {
        if (ordered && context.Database.IsNpgsql())
        {
            // WHY: PostgreSQL needs an explicit nondeterministic ICU collation to exercise database-equal
            // spellings while the imported provider representations still differ.
            modelBuilder.HasCollation(
                "broad_node_key_ci",
                locale: "und-u-ks-level2",
                provider: "icu",
                deterministic: false);
        }

        var collation = context.Database.IsSqlite()
            ? ordered
                ? "NOCASE"
                : "BINARY"
            : context.Database.IsSqlServer()
                ? ordered
                    ? "Latin1_General_100_CI_AS"
                    : "Latin1_General_100_BIN2"
                : context.Database.IsNpgsql()
                    ? ordered
                        ? "broad_node_key_ci"
                        : "C"
                    : ordered
                        ? "utf8mb4_general_ci"
                        : "utf8mb4_bin";

        var node = modelBuilder.Entity<BroadNodeKeyNode>();
        node.ToTable(table);
        node.HasKey(entity => entity.Id);
        var key = node
            .Property(entity => entity.Id)
            .HasConversion(value => value.Value, value => new BroadNodeKey(value))
            .HasMaxLength(40)
            .UseCollation(collation)
            .ValueGeneratedNever();

        var parent = node
            .Property(entity => entity.ParentId)
            .HasConversion(
                value => value == null
                    ? null
                    : value.Value,
                value => value == null
                    ? null
                    : new BroadNodeKey(value))
            .HasMaxLength(40)
            .UseCollation(collation);

        node
            .Property(entity => entity.Name)
            .HasMaxLength(40);

        // WHY: EF relationship fixup follows each database collation, while the sorted refresh must still
        // reject changes to the actual stored representation even when the database equates the spellings.
        var comparer = new ValueComparer<BroadNodeKey>(
            (first,
                second) => ordered
                ? StringComparer.OrdinalIgnoreCase.Equals(first!.Value, second!.Value)
                : StringComparer.Ordinal.Equals(first!.Value, second!.Value),
            value => ordered
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(value.Value)
                : StringComparer.Ordinal.GetHashCode(value.Value),
            value => new BroadNodeKey(value.Value));

        key.Metadata.SetValueComparer(comparer);
        parent.Metadata.SetValueComparer(comparer);
        node
            .HasOne<BroadNodeKeyNode>()
            .WithMany()
            .HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        node.HasNestedSet(nestedSet =>
        {
            nestedSet
                .HasNodeKey(entity => entity.Id)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position);

            if (ordered)
            {
                nestedSet.OrderBy(entity => entity.Name);
            }
        });
    }
}
