namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Represents a hierarchy whose NodeKey and Parent use the same strongly typed ID conversion.</summary>
internal sealed class StrongIdNode
{
    /// <summary>Gets or sets the strongly typed node identity.</summary>
    internal StrongNodeId Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the strongly typed direct parent identity.</summary>
    internal StrongNodeId? ParentId { get; set; }

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

/// <summary>Maps strong NodeKey and Parent values to compatible relational integer columns.</summary>
internal sealed class StrongIdContext : DbContext
{
    /// <summary>Creates a value-converter compatibility context.</summary>
    internal StrongIdContext(
        DbContextOptions<StrongIdContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<StrongIdNode>();
        node
            .Property(entity => entity.Id)
            .HasConversion(id => id.Value, value => new StrongNodeId(value))
            .ValueGeneratedNever();
        node
            .Property(entity => entity.ParentId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (int?)null,
                value => value.HasValue ? new StrongNodeId(value.Value) : null);
        node
            .HasOne<StrongIdNode>()
            .WithMany()
            .HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }
}
