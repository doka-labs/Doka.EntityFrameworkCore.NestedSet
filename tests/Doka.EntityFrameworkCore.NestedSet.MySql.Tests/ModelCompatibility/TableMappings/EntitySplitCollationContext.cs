namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Maps binary table identities over a CI model default without explicit identity-column facets.</summary>
internal sealed class EntitySplitCollationContext
    : DbContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public EntitySplitCollationContext(
        DbContextOptions<EntitySplitCollationContext> options
    ) : base(options) { }

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
