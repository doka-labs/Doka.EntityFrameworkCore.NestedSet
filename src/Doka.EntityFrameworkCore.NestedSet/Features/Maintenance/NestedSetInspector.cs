namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

/// <summary>Inspects ordered adjacency and derives coordinates without database access.</summary>
/// <typeparam name="TKey">The mapped primary key type.</typeparam>
internal static class NestedSetInspector<TKey>
    where TKey : notnull
{
    /// <summary>Compares persisted coordinates with canonical adjacency and sibling order.</summary>
    /// <param name="nodes">The structural snapshot, with one row per primary key.</param>
    /// <param name="parents">Child-to-parent links resolved using the database's primary-key comparison rules.</param>
    /// <param name="keyComparer">The mapped key's equality semantics, including structural binary-key equality.</param>
    /// <param name="collectRepairs">Whether changed rows should be retained for a subsequent rebuild.</param>
    /// <param name="cancellationToken">The token checked while grouping and traversing the tree.</param>
    /// <param name="collectIssues">Whether node-specific diagnostics should be retained.</param>
    /// <param name="ordered">Whether database ordering already defines each sibling group's intended order.</param>
    /// <param name="issueLimit">Maximum retained issue samples; every violation is still counted.</param>
    /// <returns>Validation errors and, when requested, the changed derived coordinates.</returns>
    /// <exception cref="OverflowException">
    ///     The tree needs more boundaries than a 64-bit coordinate can represent.
    /// </exception>
    /// <exception cref="OperationCanceledException">The cancellation token is canceled.</exception>
    internal static NestedSetInspection<TKey> Inspect(
        NestedSetNode<TKey>[] nodes,
        Dictionary<TKey, TKey>? parents,
        IEqualityComparer<TKey> keyComparer,
        bool collectRepairs,
        CancellationToken cancellationToken,
        bool ordered = false,
        bool collectIssues = false,
        int issueLimit = NestedSetIssueCollector<TKey>.ReportLimit
    )
    {
        _ = checked((long)nodes.Length * 2);

        var children = new Dictionary<TKey, List<int>>(keyComparer);
        var roots = new List<int>();
        var errors = new List<string>();
        var issues = collectIssues ? new NestedSetIssueCollector<TKey>(issueLimit) : null;
        var keys = parents is null
            ? new HashSet<TKey>(nodes.Select(node => node.Key), keyComparer)
            : null;

        for (var index = 0; index < nodes.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var node = nodes[index];
            if (!ordered
                && node.Position < 0)
            {
                AddIssue(
                    errors,
                    issues,
                    NestedSetValidationCode.NegativePosition,
                    node.Key,
                    "Sibling position must be non-negative.");
            }

            if (!node.Parent.HasValue)
            {
                roots.Add(index);
            }
            else if (ResolveParent(node, parents, keys, out var parent))
            {
                if (!children.TryGetValue(parent, out var siblings))
                {
                    // WHY: Deep chains have one child per parent; reserving one slot avoids unused list capacity.
                    siblings = new List<int>(1);
                    children.Add(parent, siblings);
                }

                siblings.Add(index);
            }
            else
            {
                AddIssue(
                    errors,
                    issues,
                    NestedSetValidationCode.MissingParent,
                    node.Key,
                    "A parent is missing from the node's tree.");
            }
        }

        var siblingComparer = Comparer<int>.Create((
            first,
            second
        ) => nodes[first]
            .Position
            .CompareTo(nodes[second].Position));

        if (!ordered)
        {
            foreach (var siblings in children.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SortSiblings(nodes, siblings, siblingComparer, errors, issues);
            }

            SortSiblings(nodes, roots, siblingComparer, errors, issues);
        }

        if (roots.Count != 1)
        {
            const string message = "A TreeId must contain exactly one root.";
            errors.Add(message);
            issues?.AddTree(NestedSetValidationCode.InvalidRootCount, message);

            if (issues is not null
                && roots.Count > 1)
            {
                foreach (var root in roots)
                {
                    issues.Add(NestedSetValidationCode.InvalidRootCount, nodes[root].Key, message);
                }
            }
        }

        // WHY: A database-ordered snapshot already puts each parent's children in rule order. Retaining those
        // indexes avoids CLR collation differences and lets strict rebuild repair obsolete or duplicate positions.

        var repairs = new List<NestedSetRepair<TKey>>();
        var visited = new bool[nodes.Length];
        var stack = new Stack<TraversalFrame>();
        var visitedCount = 0;
        long boundary = 0;
        var differences = CoordinateDifference.None;

        // WHY: A cursor retains one sibling group per ancestor instead of pushing every sibling onto the stack.
        // Wide trees therefore consume traversal-stack space proportional to depth, not the number of nodes.
        stack.Push(new TraversalFrame(roots, 0, -1, 0, -1, 0));

        while (stack.TryPop(out var frame))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (frame.NextSibling == frame.Siblings.Count)
            {
                if (frame.ParentIndex >= 0)
                {
                    boundary = checked(boundary + 1);
                    differences |= Compare(
                        nodes[frame.ParentIndex],
                        new NestedSetRepair<TKey>(
                            nodes[frame.ParentIndex].Key,
                            frame.ParentLeft,
                            boundary,
                            frame.ParentDepth,
                            frame.ParentPosition),
                        collectRepairs,
                        repairs,
                        issues,
                        ordered);
                }

                continue;
            }

            var nodeIndex = frame.Siblings[frame.NextSibling];
            if (visited[nodeIndex])
            {
                AddIssue(
                    errors,
                    issues,
                    NestedSetValidationCode.CycleOrUnreachableNode,
                    nodes[nodeIndex].Key,
                    "Adjacency contains a cycle.");
                break;
            }

            visited[nodeIndex] = true;
            visitedCount++;
            stack.Push(frame with { NextSibling = frame.NextSibling + 1 });

            var node = nodes[nodeIndex];
            boundary = checked(boundary + 1);
            var left = boundary;
            var depth = checked(frame.ParentDepth + 1);
            if (children.TryGetValue(node.Key, out var descendants))
            {
                stack.Push(new TraversalFrame(descendants, 0, nodeIndex, left, depth, frame.NextSibling));
            }
            else
            {
                boundary = checked(boundary + 1);
                differences |= Compare(
                    node,
                    new NestedSetRepair<TKey>(node.Key, left, boundary, depth, frame.NextSibling),
                    collectRepairs,
                    repairs,
                    issues,
                    ordered);
            }
        }

        if (visitedCount != nodes.Length)
        {
            errors.Add("Adjacency contains a cycle or an unreachable node.");

            if (issues is not null)
            {
                for (var index = 0; index < nodes.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!visited[index])
                    {
                        issues.Add(
                            NestedSetValidationCode.CycleOrUnreachableNode,
                            nodes[index].Key,
                            "Adjacency contains a cycle or an unreachable node.");
                    }
                }
            }
        }

        // WHY: Gaps and corrupt derived coordinates are repairable; ambiguous adjacency must never be persisted.
        var canRebuild = errors.Count == 0;
        if (canRebuild)
        {
            if ((differences & CoordinateDifference.Intervals) != 0)
            {
                errors.Add("Intervals do not match the ordered adjacency tree.");
            }

            if ((differences & CoordinateDifference.Depth) != 0)
            {
                errors.Add("Depths do not match the ordered adjacency tree.");
            }

            if ((differences & CoordinateDifference.Position) != 0)
            {
                errors.Add(
                    ordered
                        ? "Sibling positions do not match the configured ordering rule."
                        : "Sibling position is not dense and zero-based.");
            }
        }

        return new NestedSetInspection<TKey>(
            canRebuild,
            nodes.LongLength,
            errors,
            repairs,
            issues?.Issues ?? [],
            issues?.TreeIssues ?? [],
            issues?.Counts ?? new Dictionary<NestedSetValidationCode, long>(),
            issues?.TotalCount ?? 0);
    }

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
