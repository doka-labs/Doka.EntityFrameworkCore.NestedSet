namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a node carrying inline EF complex payload.</summary>
internal sealed class ComplexPayloadNode
{
    /// <summary>Gets or sets the stable node identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identity.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets inline complex application payload.</summary>
    internal ComplexNodeDetails Details { get; set; } = new();
}
