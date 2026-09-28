namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Contains payload stored as an EF owned entity in the node table.</summary>
internal sealed class OwnedNodeDetails
{
    /// <summary>Gets or sets payload text.</summary>
    internal string Label { get; set; } = "";
}

/// <summary>Contains inline complex payload without a separate entity identity.</summary>
internal sealed class ComplexNodeDetails
{
    /// <summary>Gets or sets payload text.</summary>
    internal string Label { get; set; } = "";
}

/// <summary>Represents a node carrying an owned payload aggregate.</summary>
internal sealed class OwnedPayloadNode
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

    /// <summary>Gets or sets the owned application payload.</summary>
    internal OwnedNodeDetails Details { get; set; } = new();
}

/// <summary>Represents a node carrying inline EF complex payload.</summary>
internal sealed class ComplexPayloadNode
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

    /// <summary>Gets or sets inline complex application payload.</summary>
    internal ComplexNodeDetails Details { get; set; } = new();
}

/// <summary>Maps owned and complex payload beside independent nested-set entities.</summary>
internal sealed class PayloadContext : DbContext
{
    /// <summary>Creates a payload compatibility context.</summary>
    internal PayloadContext(
        DbContextOptions<PayloadContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var owned = modelBuilder.Entity<OwnedPayloadNode>();
        owned.ToTable("OwnedPayloadNodes");
        owned
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        owned.OwnsOne(entity => entity.Details, details => details.Property(value => value.Label));
        Configure(owned);

        var complex = modelBuilder.Entity<ComplexPayloadNode>();
        complex.ToTable("ComplexPayloadNodes");
        complex
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        complex.ComplexProperty(entity => entity.Details, details => details.Property(value => value.Label));
        Configure(complex);
    }

    /// <summary>Maps the structural roles of one payload-carrying node type.</summary>
    private static void Configure(
        EntityTypeBuilder<OwnedPayloadNode> node
    )
    {
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }

    /// <summary>Maps the structural roles of the complex-payload node.</summary>
    private static void Configure(
        EntityTypeBuilder<ComplexPayloadNode> node
    )
    {
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }
}
