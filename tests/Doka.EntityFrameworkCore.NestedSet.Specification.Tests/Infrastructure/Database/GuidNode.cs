namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>An optional-interface entity with a Guid key and nullable Guid parent.</summary>
public sealed class GuidNode : IScopedNestedSetNode<Guid, Guid, int>
{
    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Tree { get; set; }

    /// <summary>Gets or sets the direct parent key, or null for a root.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    int IScopedNestedSetNode<Guid, Guid, int>.Scope => Tree;

    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the left interval boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right interval boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth from the forest root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the zero-based position among siblings.</summary>
    public long Position { get; set; }
}
