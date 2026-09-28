namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Describes the structural validity of one exact tree.</summary>
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
        IssueCounts = new System.Collections.ObjectModel.ReadOnlyDictionary<NestedSetValidationCode, long>(
            issueCounts is null
                ? issues
                    .GroupBy(issue => issue.Code)
                    .ToDictionary(group => group.Key, group => (long)group.Count())
                : new Dictionary<NestedSetValidationCode, long>(issueCounts));

        TotalIssueCount = totalIssueCount < 0 ? issues.Count : totalIssueCount;
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

/// <summary>Identifies one structural violation in a tree-bound validation report.</summary>
public sealed class NestedSetValidationIssue
{
    /// <summary>Initializes one immutable structural issue.</summary>
    /// <param name="code">The stable machine-readable violation category.</param>
    /// <param name="nodeKey">The offending node key, or <see langword="null" /> for a tree-wide issue.</param>
    /// <param name="message">A payload-free explanation suitable for diagnostics and operator reports.</param>
    internal NestedSetValidationIssue(
        NestedSetValidationCode code,
        object? nodeKey,
        string message
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        Code = code;
        NodeKey = nodeKey;
        Message = message;
    }

    /// <summary>Gets the stable machine-readable violation category.</summary>
    public NestedSetValidationCode Code { get; }

    /// <summary>Gets the offending node key, or <see langword="null" /> for a tree-wide issue.</summary>
    public object? NodeKey { get; }

    /// <summary>Gets the payload-free explanation suitable for diagnostics and operator reports.</summary>
    public string Message { get; }
}
