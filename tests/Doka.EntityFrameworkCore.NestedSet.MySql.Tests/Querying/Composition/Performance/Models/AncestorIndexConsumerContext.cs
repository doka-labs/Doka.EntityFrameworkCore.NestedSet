namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Retains a scalar-key hierarchy and independent consumer occurrences without a foreign-key optimizer shortcut.</summary>
/// <param name="options">The provider-owned database and observers.</param>
internal sealed class AncestorIndexConsumerContext(DbContextOptions<AncestorIndexConsumerContext> options)
    : DbContext(options)
{
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

/// <summary>Maps only the scalar hierarchy coordinates used by the native regression.</summary>
internal sealed class AncestorIndexConsumerNode
{
    /// <summary>Gets or sets the globally unique node key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the fixture scope.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the tree identity.</summary>
    public int Tree { get; set; }

    /// <summary>Gets or sets the independently seeded direct parent.</summary>
    public int? Parent { get; set; }

    /// <summary>Gets or sets the opening traversal coordinate.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the closing traversal coordinate.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the independently seeded depth.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets the database-generated width classification.</summary>
    public bool IsNonLeaf { get; set; }
}

/// <summary>Represents a consumer occurrence independently of the selected hierarchy node.</summary>
internal sealed class AncestorIndexConsumerOccurrence
{
    /// <summary>Gets or sets the occurrence identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the referenced node key.</summary>
    public int Node { get; set; }

    /// <summary>Gets or sets the ordinary consumer selection label.</summary>
    public string Selection { get; set; } = string.Empty;
}
