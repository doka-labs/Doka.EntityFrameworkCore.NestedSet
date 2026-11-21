namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps a full CLR self-navigation graph to verify that failed imports leave input graphs reusable.</summary>
public sealed class BulkNavigationNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the nullable parent foreign key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the optional mapped parent navigation.</summary>
    public BulkNavigationNode? Parent { get; set; }

    /// <summary>Gets the initially empty mapped child navigation.</summary>
    public List<BulkNavigationNode> Children { get; } = [];

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Scope { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
