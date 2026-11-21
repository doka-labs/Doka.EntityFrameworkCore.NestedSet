namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>An optional-interface entity used to exercise string-key collation.</summary>
public sealed class TextNode : IScopedNestedSetNode<string, Guid, string>
{
    /// <summary>Gets or sets the isolated forest scope.</summary>
    public string Tree { get; set; } = "";

    /// <summary>Gets or sets the direct parent key, or null for a root.</summary>
    public string? ParentId { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    string IScopedNestedSetNode<string, Guid, string>.Scope => Tree;

    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public string Id { get; set; } = "";

    /// <summary>Gets or sets the left interval boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right interval boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth from the forest root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the zero-based position among siblings.</summary>
    public long Position { get; set; }
}
