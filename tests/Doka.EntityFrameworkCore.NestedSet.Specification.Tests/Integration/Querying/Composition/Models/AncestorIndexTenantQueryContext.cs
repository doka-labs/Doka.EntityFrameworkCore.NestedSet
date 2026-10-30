namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Retains application-owned tenant visibility independently of explicit hierarchy bindings.</summary>
/// <param name="options">The fixture-owned provider connection and observers.</param>
internal sealed class AncestorIndexTenantQueryContext(DbContextOptions<AncestorIndexTenantQueryContext> options)
    : DbContext(options)
{
    /// <summary>Gets or sets the application partition used by the ordinary global query filter.</summary>
    internal Guid Tenant { get; set; }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<CompositionNode>();
        node.ToTable("AncestorIndexTenantNodes");
        node.HasKey(value => new { value.Scope, value.Id });
        node.Property(value => value.Id).ValueGeneratedNever();
        node.Property(value => value.Visible);
        node.HasQueryFilter(value => value.Scope == Tenant && value.Visible);
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(value => value.Id)
            .HasScope(value => value.Scope)
            .HasTreeId(value => value.TreeId)
            .HasParent(value => value.ParentId)
            .HasBounds(value => value.Left, value => value.Right)
            .HasDepth(value => value.Depth)
            .HasPosition(value => value.Position)
            .HasAncestorIndex());
    }
}
