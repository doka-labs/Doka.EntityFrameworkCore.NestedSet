namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Builds one deliberately varied model for mapping acceptance and rejection tests.</summary>
public sealed class InvalidContext : DbContext, ITestModelVariant
{
    /// <summary>Creates a context with the requested mapping variation.</summary>
    /// <param name="options">The SQLite options, including the variant-aware model cache.</param>
    /// <param name="variant">The mapping variation to apply in model construction.</param>
    public InvalidContext(
        DbContextOptions<InvalidContext> options,
        string variant
    ) : base(options)
    {
        Variant = variant;
    }

    /// <summary>Gets the mapping variation included in the EF model cache key.</summary>
    public string Variant { get; }

    /// <inheritdoc />
    object ITestModelVariant.ModelVariant => Variant;

    /// <summary>Configures the selected valid or invalid model without sharing it across test variants.</summary>
    /// <param name="modelBuilder">The builder for this context's model.</param>
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        if (Variant is "nullable-scope" or "parent-collation-without-key-collation" or "model-collation")
        {
            var text = modelBuilder.Entity<TextNode>();
            text.HasNestedSet(x => x
                .HasTreeId(n => n.TreeId)
                .HasScope(n => n.Tree)
                .HasParent(n => n.ParentId));

            if (Variant == "nullable-scope")
            {
                text
                    .Property(x => x.Tree)
                    .IsRequired(false);
            }
            else if (Variant == "model-collation")
            {
                // Different collations require parent comparisons to preserve the key's equality semantics.
                modelBuilder.UseCollation("NOCASE");
                text
                    .Property(x => x.ParentId)
                    .UseCollation("BINARY");
            }
            else
            {
                text
                    .Property(x => x.ParentId)
                    .UseCollation("NOCASE");
            }

            return;
        }

        if (Variant == "generated-key")
        {
            modelBuilder
                .Entity<TreeNode>()
                .ToTable("ContractGeneratedNodes");
        }

        var entity = modelBuilder.Entity<TreeNode>();

        if (Variant == "keyless")
        {
            entity.HasNoKey();
            entity.HasNestedSet(x => x
                .HasNodeKey(n => n.NodeId)
                .HasBounds(n => n.Start, n => n.End)
                .HasDepth(n => n.Depth)
                .HasTreeId(n => n.TreeId)
                .HasScope(n => n.Tree)
                .HasParent(n => n.Parent)
                .HasPosition(n => n.Position));

            return;
        }

        entity.HasKey(x => x.NodeId);
        entity.HasNestedSet(x => x
            .HasBounds(n => n.Start, n => n.End)
            .HasDepth(n => n.Depth)
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.Parent)
            .HasPosition(n => n.Position));

        switch (Variant)
        {
            case "filter":
                entity.HasQueryFilter(x => x.Tree != -1);
                break;
            case "unique-boundary":
                entity
                    .HasIndex(x => new
                    {
                        x.Tree,
                        x.Start,
                    })
                    .IsUnique();
                break;
            case "unique-sibling":
                entity
                    .HasIndex(x => new
                    {
                        x.Tree,
                        x.Parent,
                        x.Position,
                    })
                    .IsUnique();
                break;
            case "unique-depth":
                entity
                    .HasIndex(x => new
                    {
                        x.Tree,
                        x.Depth,
                    })
                    .IsUnique();
                break;
            case "generated-boundary":
                entity
                    .Property(x => x.Start)
                    .ValueGeneratedOnAdd();
                break;
            case "generated-depth":
                entity
                    .Property(x => x.Depth)
                    .ValueGeneratedOnAdd();
                break;
            case "generated-position":
                entity
                    .Property(x => x.Position)
                    .ValueGeneratedOnAdd();
                break;
            case "converted-key":
                entity
                    .Property(x => x.NodeId)
                    .HasConversion<string>();
                break;
            case "converted-scope":
                entity
                    .Property(x => x.Tree)
                    .HasConversion<string>();
                break;
            case "converted-parent":
                entity
                    .Property(x => x.Parent)
                    .HasConversion<string>();
                break;
            case "converted-position":
                entity
                    .Property(x => x.Position)
                    .HasConversion<string>();
                break;
            case "converted-depth":
                entity
                    .Property(x => x.Depth)
                    .HasConversion<string>();
                break;
            case "converted-boundary":
                entity
                    .Property(x => x.Start)
                    .HasConversion<string>();
                break;
            case "duplicate-role":
                entity.HasNestedSet(x => x
                    .HasBounds(n => n.Start, n => n.Start)
                    .HasDepth(n => n.Depth)
                    .HasTreeId(n => n.TreeId)
                    .HasScope(n => n.Tree)
                    .HasParent(n => n.Parent)
                    .HasPosition(n => n.Position));
                break;
            case "duplicate-depth-role":
                // WHY: Structural roles now have distinct CLR widths, so alias another int role deliberately.
                entity.HasNestedSet(x => x
                    .HasTreeId(n => n.TreeId)
                    .HasDepth(n => n.Tree));
                break;
            case "duplicate-position-role":
                // WHY: Structural roles now have distinct CLR widths, so alias another long role deliberately.
                entity.HasNestedSet(x => x
                    .HasTreeId(n => n.TreeId)
                    .HasPosition(n => n.Start));
                break;
            case "required-parent":
                entity
                    .Property(x => x.Parent)
                    .IsRequired();
                break;
            case "split":
                entity.SplitToTable(
                    "TreePositions",
                    split =>
                    {
                        split.Property(x => x.Start);
                        split.Property(x => x.End);
                    });
                break;
            case "inheritance":
                modelBuilder
                    .Entity<DerivedTreeNode>()
                    .HasBaseType<TreeNode>();
                break;
            case "shared-table":
                entity.ToTable("SharedNodes");
                modelBuilder
                    .Entity<SharedNode>()
                    .ToTable("SharedNodes")
                    .HasOne<TreeNode>()
                    .WithOne()
                    .HasForeignKey<SharedNode>(x => x.Id);
                break;
            case "cascade":
                entity
                    .HasOne<TreeNode>()
                    .WithMany()
                    .HasForeignKey(x => x.Parent)
                    .OnDelete(DeleteBehavior.Cascade);
                break;
        }
    }
}
