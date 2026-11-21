using System.Collections.ObjectModel;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Describes the structural validity of one exact tree.</summary>
[PublicAPI]
public sealed class NestedSetValidationReport
{
    /// <summary>Initializes one immutable tree validation report.</summary>
    /// <param name="level">The requested validation level.</param>
    /// <param name="nodeCount">The number of inspected nodes.</param>
    /// <param name="issues">The retained structural issue sample.</param>
    /// <param name="issueCounts">Complete counts by stable violation code, including omitted samples.</param>
    /// <param name="totalIssueCount">The total number of violations, including omitted samples.</param>
    internal NestedSetValidationReport(
        NestedSetValidationLevel level,
        long nodeCount,
        IReadOnlyList<NestedSetValidationIssue> issues,
        IReadOnlyDictionary<NestedSetValidationCode, long>? issueCounts = null,
        long totalIssueCount = -1
    )
    {
        ArgumentNullException.ThrowIfNull(issues);

        Level = level;
        NodeCount = nodeCount;
        Issues = Array.AsReadOnly(issues.ToArray());
        IssueCounts = new ReadOnlyDictionary<NestedSetValidationCode, long>(
            issueCounts is null
                ? issues
                    .GroupBy(issue => issue.Code)
                    .ToDictionary(group => group.Key, group => (long)group.Count())
                : new Dictionary<NestedSetValidationCode, long>(issueCounts));

        TotalIssueCount = totalIssueCount < 0
            ? issues.Count
            : totalIssueCount;
    }

    /// <summary>Gets the validation detail used to produce this report.</summary>
    public NestedSetValidationLevel Level { get; }

    /// <summary>Gets the number of inspected nodes.</summary>
    public long NodeCount { get; }

    /// <summary>Gets up to 1,024 structural issues from a full inspection.</summary>
    public IReadOnlyList<NestedSetValidationIssue> Issues { get; }

    /// <summary>
    /// Gets complete counts by code; root-count failures include the tree-wide issue and every root when several exist.
    /// </summary>
    public IReadOnlyDictionary<NestedSetValidationCode, long> IssueCounts { get; }

    /// <summary>Gets the total count, including tree-wide and per-root diagnostics for a root-count failure.</summary>
    public long TotalIssueCount { get; }

    /// <summary>Gets whether the bounded issue sample omits any individual violations.</summary>
    public bool IssuesTruncated => TotalIssueCount > Issues.Count;

    /// <summary>Gets whether the tree satisfies every checked structural invariant.</summary>
    public bool IsValid => TotalIssueCount == 0;
}
