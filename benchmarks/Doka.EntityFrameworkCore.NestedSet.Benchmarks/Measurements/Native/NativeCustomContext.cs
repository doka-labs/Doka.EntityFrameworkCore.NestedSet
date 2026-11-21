namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Maps the original interface-free custom-role hierarchy without borrowing benchmark interface fast paths.
/// </summary>
internal sealed class NativeCustomContext : DbContext
{
    /// <summary>Uses the owned provider and catalog supplied by the existing benchmark launcher.</summary>
    internal NativeCustomContext(DbContextOptions options) : base(options) { }

    /// <summary>Gets the custom-role node root used by independent persisted-state verification.</summary>
    internal DbSet<NativeCustomNode> Nodes => Set<NativeCustomNode>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsNpgsql())
        {
            modelBuilder.HasDefaultSchema("hierarchies");
        }

        var node = modelBuilder.Entity<NativeCustomNode>();
        node.HasKey(value => value.NodeId);
        node.Property(value => value.NodeId).ValueGeneratedNever();
        node.HasNestedSet(mapping => mapping.HasNodeKey(value => value.NodeId)
            .HasBounds(value => value.Start, value => value.End).HasDepth(value => value.Depth)
            .HasTreeId(value => value.TreeId).HasScope(value => value.Tree).HasParent(value => value.Parent)
            .HasPosition(value => value.Position));
        modelBuilder.Entity<NativeCustomMarker>().Property(value => value.Id).ValueGeneratedNever();
    }
}
