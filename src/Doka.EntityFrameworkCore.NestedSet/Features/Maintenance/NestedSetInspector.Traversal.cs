namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

internal static partial class NestedSetInspector<TKey>
    where TKey : notnull
{
    /// <summary>Sorts one sibling group and records ambiguous positions while accepting repairable gaps.</summary>
    /// <param name="nodes">The structural snapshot indexed by the sibling group.</param>
    /// <param name="siblings">Child indexes sharing a canonical parent, or the selected tree's root candidates.</param>
    /// <param name="comparer">The shared position comparer, avoiding one captured delegate per sibling group.</param>
    /// <param name="errors">The destination for an ambiguous-order diagnostic.</param>
    /// <param name="issues">Optional node-specific diagnostics.</param>
    private static void SortSiblings(
        NestedSetNode<TKey>[] nodes,
        List<int> siblings,
        IComparer<int> comparer,
        List<string> errors,
        NestedSetIssueCollector<TKey>? issues
    )
    {
        siblings.Sort(comparer);

        // WHY: Compare adjacent sorted sibling positions without another projection or buffer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 1; index < siblings.Count; index++)
        {
            if (nodes[siblings[index - 1]].Position == nodes[siblings[index]].Position)
            {
                AddIssue(
                    errors,
                    issues,
                    NestedSetValidationCode.DuplicatePosition,
                    nodes[siblings[index]].Key,
                    "Sibling positions must be unique within each parent, including roots.");
            }
        }
    }

    /// <summary>Accumulates coordinate differences and retains only changed rows when rebuilding.</summary>
    /// <param name="node">The original persisted structural row.</param>
    /// <param name="expected">The reconstructed coordinates, stored only when the row requires a repair.</param>
    /// <param name="collectRepairs">Whether a changed row should be retained.</param>
    /// <param name="repairs">The destination for changed rows.</param>
    /// <param name="issues">Optional node-specific diagnostics.</param>
    /// <param name="ordered">Whether a configured rule defines sibling order.</param>
    /// <returns>The kinds of coordinate differences found in this row.</returns>
    private static CoordinateDifference Compare(
        NestedSetNode<TKey> node,
        NestedSetRepair<TKey> expected,
        bool collectRepairs,
        List<NestedSetRepair<TKey>> repairs,
        NestedSetIssueCollector<TKey>? issues,
        bool ordered
    )
    {
        var differences = CoordinateDifference.None;

        if (node.Left != expected.Left
            || node.Right != expected.Right)
        {
            differences |= CoordinateDifference.Intervals;
            issues?.Add(
                NestedSetValidationCode.InvalidBounds,
                node.Key,
                "Intervals do not match the ordered adjacency tree.");
        }

        if (node.Depth != expected.Depth)
        {
            differences |= CoordinateDifference.Depth;
            issues?.Add(
                NestedSetValidationCode.InvalidDepth,
                node.Key,
                "Depths do not match the ordered adjacency tree.");
        }

        if (node.Position != expected.Position)
        {
            differences |= CoordinateDifference.Position;
            issues?.Add(
                NestedSetValidationCode.InvalidPosition,
                node.Key,
                ordered
                    ? "Sibling positions do not match the configured ordering rule."
                    : "Sibling position is not dense and zero-based.");
        }

        if (collectRepairs && differences != CoordinateDifference.None)
        {
            // WHY: Validation needs only three flags; a rebuild retains coordinates only for rows it must update.
            repairs.Add(expected);
        }

        return differences;
    }

    /// <summary>Resolves native keys locally or uses database-canonical parent aliases when required.</summary>
    private static bool ResolveParent(
        NestedSetNode<TKey> node,
        Dictionary<TKey, TKey>? parents,
        HashSet<TKey>? keys,
        out TKey parent
    )
    {
        if (parents is not null)
        {
            return parents.TryGetValue(node.Key, out parent!);
        }

        parent = node.Parent.Value;

        return keys!.Contains(parent);
    }

    /// <summary>Retains deduplicated diagnostic messages and optional node-specific details.</summary>
    private static void AddIssue(
        List<string> errors,
        NestedSetIssueCollector<TKey>? issues,
        NestedSetValidationCode code,
        TKey key,
        string message
    )
    {
        if (!errors.Contains(message, StringComparer.Ordinal))
        {
            errors.Add(message);
        }

        issues?.Add(code, key, message);
    }

    /// <summary>Tracks coordinate error categories without retaining a second copy of every node.</summary>
    [Flags]
    private enum CoordinateDifference
    {
        /// <summary>The row already matches its reconstructed coordinates.</summary>
        None = 0,

        /// <summary>At least one interval boundary differs.</summary>
        Intervals = 1,

        /// <summary>The persisted ancestor count differs.</summary>
        Depth = 2,

        /// <summary>The persisted sibling index differs.</summary>
        Position = 4,
    }

    /// <summary>Keeps the current sibling cursor and the coordinates needed when its parent exits.</summary>
    /// <param name="Siblings">Indexes into the structural snapshot for the current sibling group.</param>
    /// <param name="NextSibling">The next child to enter, also its reconstructed position.</param>
    /// <param name="ParentIndex">The parent snapshot index, or -1 for the root group.</param>
    /// <param name="ParentLeft">The boundary assigned when the parent was entered.</param>
    /// <param name="ParentDepth">The parent's ancestor count, or -1 for the root group.</param>
    /// <param name="ParentPosition">The parent's reconstructed index among its siblings.</param>
    private readonly record struct TraversalFrame(
        List<int> Siblings,
        int NextSibling,
        int ParentIndex,
        long ParentLeft,
        int ParentDepth,
        long ParentPosition
    );
}
