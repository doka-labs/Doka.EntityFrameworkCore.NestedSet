namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Stores the exact ConvertedIntegerGuardNode key and nullable-parent shape used by a native guard workload.
/// </summary>
internal sealed class ConvertedIntegerGuardNode
{
    /// <summary>Gets or sets the assigned node key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the independently protected tree identity.</summary>
    public NativeGuardTreeId TreeId { get; set; } = null!;

    /// <summary>Gets or sets the direct nullable parent key.</summary>
    public int? ParentId { get; set; }

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
