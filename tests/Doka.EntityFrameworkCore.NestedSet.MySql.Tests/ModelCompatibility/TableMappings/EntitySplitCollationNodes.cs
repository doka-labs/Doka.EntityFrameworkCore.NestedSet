namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Stores payload in one table and native string hierarchy identities in a secondary fragment.</summary>
internal sealed class EntitySplitCollationNode
{
    /// <summary>Gets or sets the assigned node key repeated in both table fragments.</summary>
    internal string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the Scope persisted only in the structural fragment.</summary>
    internal string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable tree identity within the Scope.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the optional parent key in the structural fragment.</summary>
    internal string? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application metadata in the primary payload fragment.</summary>
    internal string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets additional application payload in the primary fragment.</summary>
    internal string Payload { get; set; } = string.Empty;
}

/// <summary>Maps binary table identities over a CI model default without explicit identity-column facets.</summary>
internal sealed class EntitySplitCollationContext(DbContextOptions<EntitySplitCollationContext> options)
    : DbContext(options)
{
    /// <summary>Names the primary table containing application payload and the shared key.</summary>
    internal const string PayloadTable = "EntitySplitCollationPayload";

    /// <summary>Names the secondary fragment containing every hierarchy coordinate and identity.</summary>
    internal const string StructuralTable = "EntitySplitCollationStructure";

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.UseCollation("utf8mb4_unicode_ci");
        var node = modelBuilder.Entity<EntitySplitCollationNode>();
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
            .Property(value => value.Payload)
            .HasMaxLength(160);

        // WHY: Doka applies the original entity owner's canonical table facet to both physical fragments.
        // Identity columns have no explicit collation, so the secondary fragment must inherit this binary facet.
        node.HasAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
        node.ToTable(
            PayloadTable,
            fragment =>
            {
                fragment.Property(value => value.Name);
                fragment.Property(value => value.Payload);
            });

        node.SplitToTable(
            StructuralTable,
            fragment =>
            {
                fragment.Property(value => value.Scope);
                fragment.Property(value => value.TreeId);
                fragment.Property(value => value.ParentId);
                fragment.Property(value => value.Left);
                fragment.Property(value => value.Right);
                fragment.Property(value => value.Depth);
                fragment.Property(value => value.Position);
            });

        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(value => value.Id)
            .HasScope(value => value.Scope)
            .HasTreeId(value => value.TreeId)
            .HasParent(value => value.ParentId)
            .HasBounds(value => value.Left, value => value.Right)
            .HasDepth(value => value.Depth)
            .HasPosition(value => value.Position));
    }
}
