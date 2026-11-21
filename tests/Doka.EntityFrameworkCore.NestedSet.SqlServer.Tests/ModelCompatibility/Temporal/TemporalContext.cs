namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Maps hierarchy structure to a SQL Server system-versioned temporal table.</summary>
internal sealed class TemporalContext : DbContext
{
    /// <summary>Creates a temporal compatibility context.</summary>
    internal TemporalContext(
        DbContextOptions<TemporalContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<TemporalNode>();
        node.ToTable(
            "TemporalNodes",
            table => table.IsTemporal(temporal =>
            {
                temporal.HasPeriodStart("ValidFrom");
                temporal.HasPeriodEnd("ValidTo");
                temporal.UseHistoryTable("TemporalNodesHistory");
            }));

        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node.Property(entity => entity.Name);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }
}
