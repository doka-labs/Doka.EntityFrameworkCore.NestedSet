namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Leaves ancestor identity facets unset while the leaf supplies Doka's inherited table collation.</summary>
internal sealed class TptInheritedCollationContext
    : NestedSetDbContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public TptInheritedCollationContext(
        DbContextOptions<TptInheritedCollationContext> options
    ) : base(options) { }

    /// <summary>Names the ancestor table that owns every nested-set structural column.</summary>
    internal const string StructuralTable = "TptInheritedCollationStructuralNodes";

    /// <summary>Names the concrete leaf table that owns its application payload.</summary>
    internal const string PayloadTable = "TptInheritedCollationPayloadNodes";

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => Configure(modelBuilder);

    /// <summary>Maps the original-owner inheritance control shared by the valid and conflicting models.</summary>
    /// <param name="modelBuilder">The model receiving the ancestor structure and concrete payload mapping.</param>
    internal static void Configure(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<TptInheritedCollationNode>();
        node.UseTptMappingStrategy();
        node.ToTable(StructuralTable);
        node.HasKey(value => value.Id);
        node
            .Property(value => value.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        node
            .Property(value => value.Scope)
            .HasMaxLength(64)
            .IsRequired();

        node
            .Property(value => value.ParentId)
            .HasMaxLength(64);

        node
            .Property(value => value.Name)
            .HasMaxLength(100);

        node
            .HasOne<TptInheritedCollationNode>()
            .WithMany()
            .HasPrincipalKey(value => new
            {
                value.Scope,
                value.Id,
            })
            .HasForeignKey(value => new
            {
                value.Scope,
                value.ParentId,
            })
            .OnDelete(DeleteBehavior.Restrict);

        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(value => value.Id)
            .HasScope(value => value.Scope)
            .HasTreeId(value => value.TreeId)
            .HasParent(value => value.ParentId)
            .HasBounds(value => value.Left, value => value.Right)
            .HasDepth(value => value.Depth)
            .HasPosition(value => value.Position));

        // WHY: Doka reads the original mapping owner's table annotation when TPT visits an ancestor store.
        // Only the leaf declares this facet, so base-only annotation lookup would lose the real Scope identity.
        var leaf = modelBuilder.Entity<TptInheritedCollationLeaf>();
        leaf.ToTable(PayloadTable);
        leaf.HasAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
        leaf
            .Property(value => value.Payload)
            .HasMaxLength(100);
    }
}
