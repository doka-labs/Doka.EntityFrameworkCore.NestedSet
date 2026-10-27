namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps real navigation identifiers for entity and collection-DTO subtree projections.</summary>
/// <param name="options">The provider-owned connection and context-specific observers.</param>
internal sealed class CompositionNavigationContext(DbContextOptions<CompositionNavigationContext> options)
    : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<CompositionNavigationNode>();
        node.ToTable("CompositionNavigationNodes");
        node.HasKey(value => new { value.Scope, value.Id });
        node.Property(value => value.Id).ValueGeneratedNever();
        node.Property(value => value.Visible);
        node.HasQueryFilter(value => value.Visible);
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(value => value.Id)
            .HasScope(value => value.Scope)
            .HasTreeId(value => value.TreeId)
            .HasParent(value => value.ParentId)
            .HasBounds(value => value.Left, value => value.Right)
            .HasDepth(value => value.Depth)
            .HasPosition(value => value.Position));

        node.OwnsOne(value => value.Details, detail => detail.Property(value => value.Caption).HasMaxLength(80));
        node.Navigation(value => value.Details).IsRequired();
        const string ownerScope = nameof(CompositionNavigationNode.Scope);
        const string ownerKey = nameof(CompositionNavigationItem.NodeId);
        node.OwnsMany(value => value.Tags, tag =>
        {
            tag.ToTable("CompositionNavigationTags");
            tag.WithOwner().HasForeignKey(ownerScope, ownerKey);
            tag.Property<Guid>(ownerScope);
            tag.Property<int>(ownerKey);
            tag.Property(value => value.Id).ValueGeneratedNever();
            tag.HasKey(ownerScope, ownerKey, nameof(CompositionNavigationTag.Id));
            tag.Property(value => value.Value).HasMaxLength(80);
        });

        var item = modelBuilder.Entity<CompositionNavigationItem>();
        item.ToTable("CompositionNavigationItems");
        item.HasKey(value => new { value.Scope, value.Id });
        item.Property(value => value.Id).ValueGeneratedNever();
        item.Property(value => value.Label).HasMaxLength(80);
        node.HasMany(value => value.Items).WithOne()
            .HasForeignKey(value => new { value.Scope, value.NodeId }).OnDelete(DeleteBehavior.Cascade);
    }
}
