using System;

namespace Doka.NestedSet.PackageConsumers;

/// <summary>Declares ordinary payload and the public structural node contract used by each adapter probe.</summary>
internal sealed class Node : INestedSetNode<int, Guid>
{
    /// <summary>Gets or sets the database-generated node identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the independently identified tree.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets application payload.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the direct parent identity.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the left boundary maintained by NestedSet.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right boundary maintained by NestedSet.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets structural depth.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets sibling position.</summary>
    public long Position { get; set; }
}
