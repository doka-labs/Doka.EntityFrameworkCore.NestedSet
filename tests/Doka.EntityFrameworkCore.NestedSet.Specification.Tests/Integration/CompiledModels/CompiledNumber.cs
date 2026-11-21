namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>A numeric-key baseline verifying compiled metadata does not require string-specific paths.</summary>
public sealed class CompiledNumber : INestedSetNode<int, Guid>
{
    /// <summary>Gets or sets the numeric hierarchy scope.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the direct parent key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
