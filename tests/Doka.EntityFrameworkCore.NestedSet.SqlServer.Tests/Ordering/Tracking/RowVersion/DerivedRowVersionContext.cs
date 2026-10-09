namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Maps a mixed TPH hierarchy whose generated concurrency token belongs only to one subtype.</summary>
/// <param name="options">The provider options configured by the isolated fixture.</param>
public sealed class DerivedRowVersionContext(DbContextOptions<DerivedRowVersionContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<DerivedVersionBaseNode>();
        node.ToTable("DerivedVersionNodes", "generated");
        node.Property(value => value.Id).ValueGeneratedNever();
        node.Property(value => value.Name).HasMaxLength(100);
        node.Property(value => value.Payload).HasMaxLength(100);
        node.HasNestedSet(builder => builder
            .HasScope(value => value.Scope)
            .HasTreeId(value => value.TreeId)
            .HasParent(value => value.ParentId)
            .OrderBy(value => value.Name));
        modelBuilder.Entity<DerivedVersionTokenNode>().Property(value => value.Version).IsRowVersion();
        modelBuilder.Entity<DerivedVersionPlainNode>();
    }
}

/// <summary>Shares structural roles and payload across the configured base owner and its concrete subtypes.</summary>
public class DerivedVersionBaseNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <inheritdoc />
    public int Id { get; set; }

    /// <inheritdoc />
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Scope { get; set; } = 1;

    /// <inheritdoc />
    public int? ParentId { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }

    /// <summary>Gets or sets the sibling ordering value.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets ordinary payload excluded from structural refresh projections.</summary>
    public string Payload { get; set; } = "Unchanged payload";
}

/// <summary>Owns a native SQL Server rowversion absent from the configured base owner.</summary>
public sealed class DerivedVersionTokenNode : DerivedVersionBaseNode
{
    /// <summary>Gets or sets the generated token changed by every structural UPDATE.</summary>
    public byte[] Version { get; set; } = [];
}

/// <summary>Exercises a sibling subtype that does not map the generated token.</summary>
public sealed class DerivedVersionPlainNode : DerivedVersionBaseNode;
