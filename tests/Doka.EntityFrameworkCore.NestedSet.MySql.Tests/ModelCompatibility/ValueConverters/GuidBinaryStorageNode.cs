namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Exercises every hierarchy identity role with a native binary column.</summary>
internal sealed class GuidBinaryStorageNode
{
    /// <summary>Gets or sets the assigned node identity.</summary>
    internal Guid Id { get; set; }

    /// <summary>Gets or sets the independent application partition.</summary>
    internal Guid Scope { get; set; }

    /// <summary>Gets or sets the tree identity within its partition.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the nullable direct parent identity.</summary>
    internal Guid? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the ordering payload used by coordinated saves.</summary>
    internal string Name { get; set; } = "Initial";
}
