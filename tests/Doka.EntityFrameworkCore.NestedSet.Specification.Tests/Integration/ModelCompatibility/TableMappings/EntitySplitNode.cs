namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents one entity split between payload and hierarchy table fragments.</summary>
internal sealed class EntitySplitNode
{
    /// <summary>Gets or sets the shared fragment primary key and NodeKey.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets payload stored in the primary fragment.</summary>
    internal string Name { get; set; } = "";

    /// <summary>Gets or sets the stable tree identity in the hierarchy fragment.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identity in the hierarchy fragment.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary in the hierarchy fragment.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary in the hierarchy fragment.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth in the hierarchy fragment.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position in the hierarchy fragment.</summary>
    internal long Position { get; set; }
}
