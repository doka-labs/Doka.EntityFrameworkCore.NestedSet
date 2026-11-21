namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a scoped hierarchy whose text NodeKey is unique only within its tenant.</summary>
internal sealed class CompositeTextKeyNode
{
    /// <summary>Gets or sets the tenant component of the relational identity.</summary>
    internal int TenantId { get; set; }

    /// <summary>Gets or sets the tenant-local text hierarchy identity.</summary>
    internal string NodeKey { get; set; } = "";

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent NodeKey within the same tenant.</summary>
    internal string? ParentNodeKey { get; set; }

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
