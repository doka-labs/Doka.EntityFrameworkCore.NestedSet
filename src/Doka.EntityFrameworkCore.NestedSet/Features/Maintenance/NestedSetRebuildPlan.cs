using System.Collections.ObjectModel;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Describes the deterministic writes a rebuild would perform without changing the database.</summary>
public sealed class NestedSetRebuildPlan
{
    /// <summary>Initializes one immutable rebuild plan.</summary>
    /// <param name="canRebuild">Whether stored adjacency and sibling order define one unambiguous tree.</param>
    /// <param name="nodeCount">The number of nodes read from the selected tree.</param>
    /// <param name="changedNodeCount">The number of nodes whose derived coordinates would change.</param>
    /// <param name="batchCount">The number of bounded database update batches required.</param>
    /// <param name="affectedRoles">The structural roles that a rebuild can update.</param>
    /// <param name="issues">The retained structural issue sample.</param>
    /// <param name="issueCounts">Complete counts by stable violation code.</param>
    /// <param name="totalIssueCount">The total number of violations including omitted samples.</param>
    internal NestedSetRebuildPlan(
        bool canRebuild,
        long nodeCount,
        long changedNodeCount,
        int batchCount,
        IReadOnlyList<NestedSetStructuralRole> affectedRoles,
        IReadOnlyList<NestedSetValidationIssue> issues,
        IReadOnlyDictionary<NestedSetValidationCode, long> issueCounts,
        long totalIssueCount
    )
    {
        ArgumentNullException.ThrowIfNull(affectedRoles);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(issueCounts);

        CanRebuild = canRebuild;
        NodeCount = nodeCount;
        ChangedNodeCount = changedNodeCount;
        BatchCount = batchCount;
        AffectedRoles = Array.AsReadOnly(affectedRoles.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        IssueCounts = new ReadOnlyDictionary<NestedSetValidationCode, long>(
            new Dictionary<NestedSetValidationCode, long>(issueCounts));

        TotalIssueCount = totalIssueCount;
    }

    /// <summary>Gets whether stored adjacency and sibling order define one unambiguous tree.</summary>
    public bool CanRebuild { get; }

    /// <summary>Gets the number of nodes read from the selected tree.</summary>
    public long NodeCount { get; }

    /// <summary>Gets the number of nodes whose derived coordinates would change.</summary>
    public long ChangedNodeCount { get; }

    /// <summary>Gets the number of bounded database update batches required.</summary>
    public int BatchCount { get; }

    /// <summary>Gets the structural roles that a rebuild can update.</summary>
    public IReadOnlyList<NestedSetStructuralRole> AffectedRoles { get; }

    /// <summary>Gets up to 1,024 structural issues found while planning the rebuild.</summary>
    public IReadOnlyList<NestedSetValidationIssue> Issues { get; }

    /// <summary>
    /// Gets complete counts by code; root-count failures include the tree-wide issue and every root when several exist.
    /// </summary>
    public IReadOnlyDictionary<NestedSetValidationCode, long> IssueCounts { get; }

    /// <summary>Gets the total count, including tree-wide and per-root diagnostics for a root-count failure.</summary>
    public long TotalIssueCount { get; }

    /// <summary>Gets whether the bounded issue sample omits any individual violations.</summary>
    public bool IssuesTruncated => TotalIssueCount > Issues.Count;
}

/// <summary>Identifies a mapped hierarchy role without exposing model property names as string contracts.</summary>
public enum NestedSetStructuralRole
{
    /// <summary>The inclusive left preorder boundary.</summary>
    Left = 0,

    /// <summary>The inclusive right preorder boundary.</summary>
    Right = 1,

    /// <summary>The number of ancestors above a node.</summary>
    Depth = 2,

    /// <summary>The zero-based position inside a sibling group.</summary>
    Position = 3,
}
