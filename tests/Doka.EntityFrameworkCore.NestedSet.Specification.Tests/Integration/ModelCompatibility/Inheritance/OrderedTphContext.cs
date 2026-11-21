namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps one TPH hierarchy with sibling ordering declared on its base entity.</summary>
internal sealed class OrderedTphContext : DbContext
{
    /// <summary>Creates an ordered TPH compatibility context.</summary>
    internal OrderedTphContext(
        DbContextOptions<OrderedTphContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<InheritanceNode>();
        node.ToTable("OrderedTphNodes");
        node
            .HasDiscriminator<string>("NodeType")
            .HasValue<TphFolderNode>("Folder")
            .HasValue<TphMetricNode>("Metric");

        TphContext.Configure(node, ordered: true);
    }
}
