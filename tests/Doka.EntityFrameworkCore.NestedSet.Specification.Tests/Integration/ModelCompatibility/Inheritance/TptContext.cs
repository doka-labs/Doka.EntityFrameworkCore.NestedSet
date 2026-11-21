namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps structural columns to one base table and derived payload to a TPT table.</summary>
internal sealed class TptContext : DbContext
{
    /// <summary>Creates a TPT compatibility context.</summary>
    internal TptContext(
        DbContextOptions<TptContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<InheritanceNode>();
        node.UseTptMappingStrategy();
        node.ToTable("TptNodes");
        modelBuilder
            .Entity<TptFolderNode>()
            .ToTable("TptFolders")
            .Property(entity => entity.FolderKind);

        modelBuilder
            .Entity<TptMetricNode>()
            .ToTable("TptMetrics")
            .Property(entity => entity.MetricKind);

        TphContext.Configure(node);
    }
}
