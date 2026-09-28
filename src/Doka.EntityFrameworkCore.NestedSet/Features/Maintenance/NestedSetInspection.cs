namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

/// <summary>Separates invalid adjacency from repairable derived-coordinate differences.</summary>
/// <typeparam name="TKey">The mapped primary key type.</typeparam>
/// <param name="CanRebuild">Whether adjacency and sibling ordering identify an unambiguous tree.</param>
/// <param name="NodeCount">The number of nodes inspected without retaining application payload.</param>
/// <param name="Errors">All discovered adjacency and persisted-coordinate violations.</param>
/// <param name="Issues">Node-specific diagnostics requested by detailed validation.</param>
/// <param name="TreeIssues">Typed tree-wide diagnostics without a node key.</param>
/// <param name="IssueCounts">
/// Counts by code; root-count failures include a tree-wide diagnostic and one for every root when several exist.
/// </param>
/// <param name="TotalIssueCount">Complete diagnostic count including omitted samples and root-count levels.</param>
/// <param name="Repairs">Only changed rows, or an empty list when inspection did not request a repair plan.</param>
internal sealed record NestedSetInspection<TKey>(
    bool CanRebuild,
    long NodeCount,
    List<string> Errors,
    List<NestedSetRepair<TKey>> Repairs,
    List<NestedSetValidationIssue<TKey>> Issues,
    List<NestedSetValidationIssue> TreeIssues,
    IReadOnlyDictionary<NestedSetValidationCode, long> IssueCounts,
    long TotalIssueCount
)
    where TKey : notnull;
