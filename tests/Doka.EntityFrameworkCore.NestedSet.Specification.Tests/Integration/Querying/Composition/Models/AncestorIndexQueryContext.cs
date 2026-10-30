namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps an opted-in indexed hierarchy with real owned and ordinary navigation identifiers.</summary>
/// <param name="options">The fixture-owned provider connection and observers.</param>
internal sealed class AncestorIndexQueryContext(DbContextOptions<AncestorIndexQueryContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<CompositionNavigationNode>();
        node.ToTable("AncestorIndexQueryNodes");
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
            .HasPosition(value => value.Position)
            .HasAncestorIndex());

        node.OwnsOne(value => value.Details, detail => detail.Property(value => value.Caption).HasMaxLength(80));
        node.Navigation(value => value.Details).IsRequired();
        const string ownerScope = nameof(CompositionNavigationNode.Scope);
        const string ownerKey = nameof(CompositionNavigationItem.NodeId);
        node.OwnsMany(value => value.Tags, tag =>
        {
            tag.ToTable("AncestorIndexQueryTags");
            tag.WithOwner().HasForeignKey(ownerScope, ownerKey);
            tag.Property<Guid>(ownerScope);
            tag.Property<int>(ownerKey);
            tag.Property(value => value.Id).ValueGeneratedNever();
            tag.HasKey(ownerScope, ownerKey, nameof(CompositionNavigationTag.Id));
            tag.Property(value => value.Value).HasMaxLength(80);
        });

        var item = modelBuilder.Entity<CompositionNavigationItem>();
        item.ToTable("AncestorIndexQueryItems");
        item.HasKey(value => new { value.Scope, value.Id });
        item.Property(value => value.Id).ValueGeneratedNever();
        item.Property(value => value.Label).HasMaxLength(80);
        node.HasMany(value => value.Items).WithOne()
            .HasForeignKey(value => new { value.Scope, value.NodeId }).OnDelete(DeleteBehavior.Cascade);
    }
}
