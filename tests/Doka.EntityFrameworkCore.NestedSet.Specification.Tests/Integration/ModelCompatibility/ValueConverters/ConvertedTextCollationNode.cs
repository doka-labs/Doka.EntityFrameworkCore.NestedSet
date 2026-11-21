namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares structural roles while retaining each key and parent's exact model types.</summary>
internal sealed class ConvertedTextCollationNode<TKey, TParent>
{
    /// <summary>Gets or sets the assigned model key.</summary>
    internal TKey Id { get; set; } = default!;

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the nullable model parent key.</summary>
    internal TParent ParentId { get; set; } = default!;

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the configured sibling ordering payload.</summary>
    internal string Name { get; set; } = string.Empty;
}
