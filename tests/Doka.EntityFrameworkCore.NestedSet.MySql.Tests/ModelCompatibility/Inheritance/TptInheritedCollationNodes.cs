namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Stores every structural identity and coordinate in the ancestor table of a TPT hierarchy.</summary>
internal abstract class TptInheritedCollationNode
{
    /// <summary>Gets or sets the assigned string node key.</summary>
    internal string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the native Scope owning the complete tree.</summary>
    internal string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable tree identity within the Scope.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the optional direct-parent string key.</summary>
    internal string? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application payload stored alongside the structural columns.</summary>
    internal string Name { get; set; } = string.Empty;
}

/// <summary>Declares the original TPT mapping owner's table collation and separately stored payload.</summary>
internal sealed class TptInheritedCollationLeaf : TptInheritedCollationNode
{
    /// <summary>Gets or sets leaf payload that must survive another Scope's root insertion.</summary>
    internal string Payload { get; set; } = string.Empty;
}

/// <summary>Leaves ancestor identity facets unset while the leaf supplies Doka's inherited table collation.</summary>
internal sealed class TptInheritedCollationContext(DbContextOptions<TptInheritedCollationContext> options)
    : NestedSetDbContext(options)
{
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
                value.Id
            })
            .HasForeignKey(value => new
            {
                value.Scope,
                value.ParentId
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

/// <summary>Declares conflicting defaults on two owners of the same physical ancestor identity columns.</summary>
internal sealed class ConflictingTptCollationContext(DbContextOptions<ConflictingTptCollationContext> options)
    : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        TptInheritedCollationContext.Configure(modelBuilder);

        // WHY: NestedSet requires one effective identity comparison for the shared physical ancestor column.
        // This deliberate invalid model differs from Doka's first-owner selection and must fail before writes.
        modelBuilder
            .Entity<TptInheritedCollationNode>()
            .HasAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_unicode_ci");
    }
}
