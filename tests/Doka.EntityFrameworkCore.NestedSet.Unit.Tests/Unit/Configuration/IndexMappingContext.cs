namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Builds one key-configuration order without sharing its model with other variants.</summary>
public sealed class IndexMappingContext : DbContext, ITestModelVariant
{
    /// <summary>Creates an isolated model used to verify maintenance-index configuration.</summary>
    /// <param name="options">The SQLite options and variant-aware model cache.</param>
    /// <param name="variant">The key-configuration order.</param>
    public IndexMappingContext(
        DbContextOptions<IndexMappingContext> options,
        string variant
    ) : base(options)
    {
        Variant = variant;
    }

    /// <summary>Gets the model variant included in the cache key.</summary>
    public string Variant { get; }

    /// <inheritdoc />
    object ITestModelVariant.ModelVariant => Variant;

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        if (Variant == "conventional")
        {
            modelBuilder
                .Entity<GuidNode>()
                .HasNestedSet(builder => builder
                    .HasTreeId(node => node.TreeId)
                    .HasScope(node => node.Tree)
                    .HasParent(node => node.ParentId));

            return;
        }

        var entity = modelBuilder.Entity<TreeNode>();

        if (Variant == "configured")
        {
            entity.HasKey(node => node.NodeId);
        }

        entity.HasNestedSet(builder =>
        {
            builder
                .HasBounds(node => node.Start, node => node.End)
                .HasDepth(node => node.Depth)
                .HasTreeId(node => node.TreeId)
                .HasScope(node => node.Tree)
                .HasParent(node => node.Parent)
                .HasPosition(node => node.Position);

            if (Variant == "explicit-before-ef-key")
            {
                builder.HasNodeKey(node => node.NodeId);
            }
        });

        entity.HasKey(node => node.NodeId);
    }
}
