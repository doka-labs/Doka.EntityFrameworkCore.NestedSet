namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a hierarchy with converted node and parent keys.</summary>
internal sealed class BroadNodeKeyNode
{
    /// <summary>Gets or sets the node identity.</summary>
    internal BroadNodeKey Id { get; set; } = null!;

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the parent identity.</summary>
    internal BroadNodeKey? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the sibling sort value when ordering is configured.</summary>
    internal string Name { get; set; } = "";
}
