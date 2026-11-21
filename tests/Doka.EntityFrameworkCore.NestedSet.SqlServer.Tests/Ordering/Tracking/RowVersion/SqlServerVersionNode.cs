namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>A generated-key hierarchy with a database-generated token changed by every real UPDATE.</summary>
public sealed class SqlServerVersionNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the direct parent key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the sort criterion.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the native SQL Server concurrency token.</summary>
    public byte[] Version { get; set; } = [];

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Scope { get; set; } = 1;

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
