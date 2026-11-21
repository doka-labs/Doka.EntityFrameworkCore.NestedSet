namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a hierarchy node with a converted reference-type TreeId.</summary>
internal sealed class BroadTreeIdNode
{
    /// <summary>Gets or sets the node key.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal BroadTreeId TreeId { get; set; } = null!;

    /// <summary>Gets or sets the parent key.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the configured sibling ordering value.</summary>
    internal string Name { get; set; } = "";
}
