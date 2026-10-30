namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exercises a required generated indexer flag together with a store-generated indexer identity.</summary>
/// <param name="options">The provider fixture's relational context options.</param>
internal sealed class AncestorIndexGeneratedBagContext(DbContextOptions<AncestorIndexGeneratedBagContext> options) : DbContext(options)
{
    /// <summary>Identifies the fixed generated-key mapping independently of other dictionary hierarchies.</summary>
    internal const string Mapping = "AncestorIndexGeneratedBag";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.SharedTypeEntity<Dictionary<string, object?>>(Mapping);
        node.ToTable(Mapping);
        node.IndexerProperty<int>(NestedSetPropertyRoles.NodeKey).ValueGeneratedOnAdd();
        node.IndexerProperty<Guid>(NestedSetPropertyRoles.TreeId);
        node.IndexerProperty<int?>(NestedSetPropertyRoles.Parent);
        node.IndexerProperty<long>(NestedSetPropertyRoles.Left);
        node.IndexerProperty<long>(NestedSetPropertyRoles.Right);
        node.IndexerProperty<int>(NestedSetPropertyRoles.Depth);
        node.IndexerProperty<long>(NestedSetPropertyRoles.Position);
        node.IndexerProperty<string?>(AncestorIndexMappedContext.Payload).HasMaxLength(80);
        node.HasKey(NestedSetPropertyRoles.NodeKey);
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(NestedSetPropertyRoles.NodeKey).HasTreeId(NestedSetPropertyRoles.TreeId)
            .HasParent(NestedSetPropertyRoles.Parent).HasBounds(NestedSetPropertyRoles.Left, NestedSetPropertyRoles.Right)
            .HasDepth(NestedSetPropertyRoles.Depth).HasPosition(NestedSetPropertyRoles.Position).HasAncestorIndex());
    }
}
