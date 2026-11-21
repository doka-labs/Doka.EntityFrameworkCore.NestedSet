namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents an ordered hierarchy whose converted NodeKey has no provider collection transport.</summary>
internal sealed class OrderedStrongIdNode
{
    /// <summary>Gets or sets the strongly typed node identity.</summary>
    internal StrongNodeId Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the strongly typed direct parent identity.</summary>
    internal StrongNodeId? ParentId { get; set; }

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
