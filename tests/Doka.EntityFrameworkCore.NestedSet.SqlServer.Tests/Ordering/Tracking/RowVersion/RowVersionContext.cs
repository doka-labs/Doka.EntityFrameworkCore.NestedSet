namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Maps a native SQL Server rowversion without substituting an application-managed counter.</summary>
public sealed class RowVersionContext : NestedSetDbContext
{
    /// <summary>Creates the context using the fixture-owned SQL Server connection.</summary>
    public RowVersionContext(
        DbContextOptions<RowVersionContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<SqlServerVersionNode>();
        node.ToTable("VersionNodes", "generated");
        node
            .Property(row => row.Version)
            .IsRowVersion();
        node
            .Property(row => row.Name)
            .HasMaxLength(100);
        node.HasNestedSet(builder => builder
            .HasTreeId(row => row.TreeId)
            .HasScope(row => row.Scope)
            .HasParent(row => row.ParentId)
            .OrderBy(row => row.Name));
    }
}

/// <summary>A generated-key hierarchy with a database-generated token changed by every real UPDATE.</summary>
public sealed class SqlServerVersionNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Scope { get; set; } = 1;

    /// <summary>Gets or sets the direct parent key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the sort criterion.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the native SQL Server concurrency token.</summary>
    public byte[] Version { get; set; } = [];

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
