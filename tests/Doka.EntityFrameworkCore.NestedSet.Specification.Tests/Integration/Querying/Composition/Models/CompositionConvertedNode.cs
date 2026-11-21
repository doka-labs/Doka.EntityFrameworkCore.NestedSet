namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses converted identities and a nullable converted parent within an independently scoped tree.</summary>
internal sealed class CompositionConvertedNode
{
    /// <summary>Gets or sets the scope component of the relational identity.</summary>
    internal Guid Scope { get; set; }

    /// <summary>Gets or sets the converted node key, including the valid zero value.</summary>
    internal CompositionNodeKey Id { get; set; }

    /// <summary>Gets or sets the converted nullable parent key.</summary>
    internal CompositionNodeKey? ParentId { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the stored structural depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    internal long Position { get; set; }
}
