namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>A string-key hierarchy with deliberately different parent and primary-key collations.</summary>
public sealed class CompiledFolder : INestedSetNode<string, Guid>
{
    /// <summary>Gets or sets the scope identifying the stable anchor.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets a parent key that may use a case alias.</summary>
    public string? ParentId { get; set; }

    /// <summary>Gets or sets the sibling sort criterion.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public string Id { get; set; } = string.Empty;

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
