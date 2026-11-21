namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Stores the exact StringGuardNode key and nullable-parent shape used by a native guard workload.</summary>
internal sealed class StringGuardNode
{
    /// <summary>Gets or sets the assigned node key.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the independently protected tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct nullable parent key.</summary>
    public string? ParentId { get; set; }

    /// <summary>Gets or sets the left traversal boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right traversal boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the root-relative depth.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets the ordinary application payload.</summary>
    public string Name { get; set; } = string.Empty;
}
