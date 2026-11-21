namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>
/// Retains a scalar-key hierarchy and independent consumer occurrences without a foreign-key optimizer shortcut.
/// </summary>
/// <param name="options">The provider-owned database and observers.</param>
internal sealed class AncestorIndexConsumerContext
    : DbContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public AncestorIndexConsumerContext(
        DbContextOptions<AncestorIndexConsumerContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<AncestorIndexConsumerNode>();
        node.ToTable("AncestorIndexConsumerNodes");
        node.HasKey(value => value.Id);
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(value => value.Id)
            .HasScope(value => value.Scope)
            .HasTreeId(value => value.Tree)
            .HasParent(value => value.Parent)
            .HasBounds(value => value.Left, value => value.Right)
            .HasDepth(value => value.Depth)
            .HasPosition(value => value.Position)
            .HasAncestorIndex(nameof(AncestorIndexConsumerNode.IsNonLeaf)));

        var occurrence = modelBuilder.Entity<AncestorIndexConsumerOccurrence>();
        occurrence.ToTable("AncestorIndexConsumerOccurrences");
        occurrence.HasKey(value => value.Id);

        // WHY: Independent application facts need not declare an EF relationship to the hierarchy.
        // This scalar-key shape reproduces source-lane miscosting independently of navigation fixtures.
    }
}
