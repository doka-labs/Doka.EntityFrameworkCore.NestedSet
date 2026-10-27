namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal sealed partial class AncestorIndexMappedContext
{
    /// <summary>Keeps native array keys and independently allocated nullable parent arrays value-comparable in SQL.</summary>
    private static void ConfigureBinary(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<BinaryNode>();
        node.ToTable("AncestorIndexBinaryNodes");
        node.Property(entity => entity.Id).HasMaxLength(16).ValueGeneratedNever();
        node.Property(entity => entity.ParentId).HasMaxLength(16);
        Configure(node, nameof(BinaryNode.Tree));
    }

    /// <summary>Separates the hierarchy's scope-local alternate key from the composite row primary key.</summary>
    private static void ConfigureAlternateKey(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<CompositeKeyNode>();
        node.ToTable("AncestorIndexAlternateNodes");
        node.HasKey(entity => new { entity.TenantId, entity.RowId });
        node.HasAlternateKey(entity => new { entity.TenantId, entity.NodeKey });
        node.Property(entity => entity.RowId).ValueGeneratedNever();
        node.Property(entity => entity.NodeKey).ValueGeneratedNever();
        node.Property(entity => entity.Name).HasMaxLength(80);
        node.HasOne<CompositeKeyNode>().WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.ParentNodeKey })
            .HasPrincipalKey(entity => new { entity.TenantId, entity.NodeKey })
            .OnDelete(DeleteBehavior.Restrict);
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(entity => entity.NodeKey).HasScope(entity => entity.TenantId)
            .HasTreeId(entity => entity.TreeId).HasParent(entity => entity.ParentNodeKey)
            .HasBounds(entity => entity.Left, entity => entity.Right).HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position).HasAncestorIndex());
    }

    /// <summary>Preserves database-distinct converted TreeIds whose CLR equality deliberately treats case aliases as equal.</summary>
    private void ConfigureBroadTree(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<BroadTreeIdNode>();
        node.ToTable("AncestorIndexBroadTreeNodes");
        node.HasKey(entity => entity.Id);
        node.Property(entity => entity.Id).ValueGeneratedNever();
        node.Property(entity => entity.Name).HasMaxLength(40);
        var tree = node.Property(entity => entity.TreeId)
            .HasConversion(value => value.Value, value => new BroadTreeId(value)).HasMaxLength(40);

        var collation = Database.IsSqlite() ? "BINARY"
            : Database.IsSqlServer() ? "Latin1_General_100_BIN2"
            : Database.IsNpgsql() ? null : "utf8mb4_bin";

        if (collation is not null)
        {
            tree.UseCollation(collation);
        }

        tree.Metadata.SetValueComparer(new ValueComparer<BroadTreeId>(
            (first, second) => StringComparer.Ordinal.Equals(first!.Value, second!.Value),
            value => StringComparer.Ordinal.GetHashCode(value.Value), value => new BroadTreeId(value.Value)));

        node.HasOne<BroadTreeIdNode>().WithMany().HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        Configure(node);
    }
}
