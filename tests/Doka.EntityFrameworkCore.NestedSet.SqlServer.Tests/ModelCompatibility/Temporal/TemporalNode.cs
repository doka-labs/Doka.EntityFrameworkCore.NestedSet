namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Represents a SQL Server temporal hierarchy node.</summary>
internal sealed class TemporalNode
{
    /// <summary>Gets or sets the stable node identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identity.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application payload.</summary>
    internal string Name { get; set; } = "";
}

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
