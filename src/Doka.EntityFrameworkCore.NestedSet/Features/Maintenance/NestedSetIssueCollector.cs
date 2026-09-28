namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

/// <summary>Counts every violation while retaining only a bounded diagnostic sample.</summary>
internal sealed class NestedSetIssueCollector<TKey>
    where TKey : notnull
{
    /// <summary>Maximum retained issue keys in a tree-bound report or rebuild plan.</summary>
    internal const int ReportLimit = 1024;

    private readonly int _limit;
    private readonly Dictionary<NestedSetValidationCode, long> _counts = new();

    /// <summary>Creates a collector with a maximum number of retained node diagnostics.</summary>
    internal NestedSetIssueCollector(
        int limit
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        _limit = limit;
    }

    /// <summary>Gets the retained node-specific diagnostics.</summary>
    internal List<NestedSetValidationIssue<TKey>> Issues { get; } = [];

    /// <summary>Gets the typed tree-wide diagnostics, which take priority over node samples.</summary>
    internal List<NestedSetValidationIssue> TreeIssues { get; } = [];

    /// <summary>Gets complete counts by stable violation code.</summary>
    internal IReadOnlyDictionary<NestedSetValidationCode, long> Counts => _counts;

    /// <summary>Gets the total number of violations, including omitted node samples.</summary>
    internal long TotalCount { get; private set; }

    /// <summary>Records a node violation without retaining every key in a damaged tree.</summary>
    internal void Add(
        NestedSetValidationCode code,
        TKey key,
        string message
    )
    {
        Count(code);

        if (Issues.Count + TreeIssues.Count < _limit)
        {
            Issues.Add(new NestedSetValidationIssue<TKey>(code, key, message));
        }
    }

    /// <summary>Records a tree-wide violation with no fabricated node key.</summary>
    internal void AddTree(
        NestedSetValidationCode code,
        string message
    )
    {
        Count(code);

        // WHY: A zero-root tree has no node key. Preserve its typed diagnostic even when earlier node
        // violations filled the bounded sample.
        if (Issues.Count + TreeIssues.Count == _limit)
        {
            Issues.RemoveAt(Issues.Count - 1);
        }

        TreeIssues.Add(new NestedSetValidationIssue(code, null, message));
    }

    private void Count(
        NestedSetValidationCode code
    )
    {
        TotalCount++;
        _counts[code] = _counts.GetValueOrDefault(code) + 1;
    }
}
