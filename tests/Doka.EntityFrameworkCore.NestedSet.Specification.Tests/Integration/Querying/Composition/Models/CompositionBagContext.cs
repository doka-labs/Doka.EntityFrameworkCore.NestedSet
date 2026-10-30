namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps two independent hierarchy names to the same indexer-backed CLR type.</summary>
internal sealed class CompositionBagContext : DbContext
{
    /// <summary>Names the selected hierarchy mapping.</summary>
    internal const string First = "CompositionFirstBag";

    /// <summary>Names the independent mapping that shares its CLR type.</summary>
    internal const string Second = "CompositionSecondBag";

    /// <summary>Creates the named shared-type context using the actual provider fixture.</summary>
    internal CompositionBagContext(
        DbContextOptions<CompositionBagContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        Configure(First, "CompositionFirstBags");
        Configure(Second, "CompositionSecondBags");

        void Configure(
            string name,
            string table
        )
        {
            var node = modelBuilder.SharedTypeEntity<Dictionary<string, object?>>(name);
            node.ToTable(table);
            node.IndexerProperty<int>(NestedSetPropertyRoles.NodeKey).ValueGeneratedNever();
            node.IndexerProperty<Guid>(NestedSetPropertyRoles.TreeId);
            node.IndexerProperty<int?>(NestedSetPropertyRoles.Parent);
            node.IndexerProperty<long>(NestedSetPropertyRoles.Left);
            node.IndexerProperty<long>(NestedSetPropertyRoles.Right);
            node.IndexerProperty<int>(NestedSetPropertyRoles.Depth);
            node.IndexerProperty<long>(NestedSetPropertyRoles.Position);
            node.HasKey(NestedSetPropertyRoles.NodeKey);
            node.HasNestedSet(mapping => mapping
                .HasNodeKey(NestedSetPropertyRoles.NodeKey)
                .HasTreeId(NestedSetPropertyRoles.TreeId)
                .HasParent(NestedSetPropertyRoles.Parent)
                .HasBounds(NestedSetPropertyRoles.Left, NestedSetPropertyRoles.Right)
                .HasDepth(NestedSetPropertyRoles.Depth)
                .HasPosition(NestedSetPropertyRoles.Position));
        }
    }
}
