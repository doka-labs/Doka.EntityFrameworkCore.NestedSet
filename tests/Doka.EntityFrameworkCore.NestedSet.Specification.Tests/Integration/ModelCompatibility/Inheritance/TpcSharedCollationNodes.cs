namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares inherited identity properties across two concrete tables with different Scope equality.</summary>
internal abstract class CollatedTpcNode
{
    /// <summary>Gets or sets the assigned key shared in metadata but stored in each concrete table.</summary>
    internal string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the independently stored namespace of each tree.</summary>
    internal string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the complete tree identity inside its Scope.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the optional direct parent under the concrete table's key comparison.</summary>
    internal string? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application payload preserved across failed hierarchy writes.</summary>
    internal string Name { get; set; } = string.Empty;
}

/// <summary>Uses case-distinct Scope identities inherited from its binary table collation.</summary>
internal sealed class BinaryTpcNode : CollatedTpcNode;

/// <summary>Uses native case-insensitive Scope aliases inherited from its own table collation.</summary>
internal sealed class CaseInsensitiveTpcNode : CollatedTpcNode;

/// <summary>Maps the same inherited Scope property independently on two concrete nested-set hierarchies.</summary>
internal sealed class CollatedTpcContext(DbContextOptions<CollatedTpcContext> options) : DbContext(options)
{
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
