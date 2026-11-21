namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps ordered structure to a TPT base table with separate derived payload tables.</summary>
internal sealed class OrderedTptContext : DbContext
{
    /// <summary>Creates an ordered TPT compatibility context.</summary>
    internal OrderedTptContext(
        DbContextOptions<OrderedTptContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<InheritanceNode>();
        node.UseTptMappingStrategy();
        node.ToTable("OrderedTptNodes");
        modelBuilder
            .Entity<TptFolderNode>()
            .ToTable("OrderedTptFolders")
            .Property(entity => entity.FolderKind);

        modelBuilder
            .Entity<TptMetricNode>()
            .ToTable("OrderedTptMetrics")
            .Property(entity => entity.MetricKind);

        TphContext.Configure(node, ordered: true);
    }
}
