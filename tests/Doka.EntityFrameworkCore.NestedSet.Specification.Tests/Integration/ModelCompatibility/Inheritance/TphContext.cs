namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps a complete polymorphic tree through one TPH table.</summary>
internal sealed class TphContext : DbContext
{
    /// <summary>Creates a TPH compatibility context.</summary>
    internal TphContext(
        DbContextOptions<TphContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<InheritanceNode>();
        node.ToTable("TphNodes");
        node
            .HasDiscriminator<string>("NodeType")
            .HasValue<TphFolderNode>("Folder")
            .HasValue<TphMetricNode>("Metric");

        Configure(node);
    }

    /// <summary>Applies the structural base mapping used by the inheritance tests.</summary>
    internal static void Configure(
        EntityTypeBuilder<InheritanceNode> node,
        bool ordered = false
    )
    {
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node.Property(entity => entity.Name);
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
