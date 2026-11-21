namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps the same inherited Scope property independently on two concrete nested-set hierarchies.</summary>
internal sealed class CollatedTpcContext : DbContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public CollatedTpcContext(
        DbContextOptions<CollatedTpcContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        // WHY: TPC properties belong to the base metadata while their physical column comparisons belong to
        // concrete tables. A property-only capture would overwrite one hierarchy's native Scope identity.
        var root = modelBuilder.Entity<CollatedTpcNode>();
        root.UseTpcMappingStrategy();
        root.HasKey(node => node.Id);
        root
            .Property(node => node.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        root
            .Property(node => node.Scope)
            .HasMaxLength(64);

        root
            .Property(node => node.ParentId)
            .HasMaxLength(64);

        root
            .Property(node => node.Name)
            .HasMaxLength(128);

        Configure(modelBuilder.Entity<BinaryTpcNode>(), "BinaryTpcScopes", "utf8mb4_bin");
        Configure(modelBuilder.Entity<CaseInsensitiveTpcNode>(), "CaseInsensitiveTpcScopes", "utf8mb4_unicode_ci");
    }

    /// <summary>Configures a concrete hierarchy whose inherited Scope has no explicit column collation.</summary>
    private static void Configure<TNode>(
        EntityTypeBuilder<TNode> node,
        string table,
        string collation
    )
        where TNode : CollatedTpcNode
    {
        node.ToTable(table);
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();

        node.HasAnnotation(RelationalAnnotationNames.Collation, collation);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(value => value.Id)
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId)
            .HasBounds(value => value.Left, value => value.Right)
            .HasDepth(value => value.Depth)
            .HasPosition(value => value.Position));
    }
}
