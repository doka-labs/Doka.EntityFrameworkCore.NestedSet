namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

internal static partial class NestedSetInspector<TKey>
    where TKey : notnull
{
    private readonly partial struct InspectionRun
    {
        /// <summary>Traverses canonical sibling groups with a cursor stack proportional to tree depth.</summary>
        internal (List<NestedSetRepair<TKey>> Repairs, CoordinateDifference Differences) Walk(
            bool collectRepairs,
            bool ordered,
            CancellationToken cancellationToken
        )
        {
            var repairs = new List<NestedSetRepair<TKey>>();
            var visited = new bool[_nodes.Length];
            var stack = new Stack<TraversalFrame>();
            var visitedCount = 0;
            long boundary = 0;
            var differences = CoordinateDifference.None;

            // WHY: A cursor retains one sibling group per ancestor instead of pushing every sibling onto the stack.
            // Wide trees therefore consume traversal-stack space proportional to depth, not the number of nodes.
            stack.Push(new TraversalFrame(_roots, 0, -1, 0, -1, 0));

            while (stack.TryPop(out var frame))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (frame.NextSibling == frame.Siblings.Count)
                {
                    if (frame.ParentIndex >= 0)
                    {
                        boundary = checked(boundary + 1);
                        differences |= Compare(
                            _nodes[frame.ParentIndex],
                            new NestedSetRepair<TKey>(
                                _nodes[frame.ParentIndex].Key,
                                frame.ParentLeft,
                                boundary,
                                frame.ParentDepth,
                                frame.ParentPosition),
                            collectRepairs,
                            repairs,
                            _issues,
                            ordered);
                    }

                    continue;
                }

                var nodeIndex = frame.Siblings[frame.NextSibling];
                if (visited[nodeIndex])
                {
                    AddIssue(
                        _errors,
                        _issues,
                        NestedSetValidationCode.CycleOrUnreachableNode,
                        _nodes[nodeIndex].Key,
                        "Adjacency contains a cycle.");

                    break;
                }

                visited[nodeIndex] = true;
                visitedCount++;
                stack.Push(frame with { NextSibling = frame.NextSibling + 1 });

                var node = _nodes[nodeIndex];
                boundary = checked(boundary + 1);
                var left = boundary;
                var depth = checked(frame.ParentDepth + 1);
                if (_children.TryGetValue(node.Key, out var descendants))
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
                        _issues,
                        ordered);
                }
            }

            ReportUnreachable(visited, visitedCount, cancellationToken);

            return (repairs, differences);
        }

        /// <summary>Reports every unreachable node without retaining a second collection of node identities.</summary>
        private void ReportUnreachable(
            bool[] visited,
            int visitedCount,
            CancellationToken cancellationToken
        )
        {
            if (visitedCount != _nodes.Length)
            {
                _errors.Add("Adjacency contains a cycle or an unreachable node.");

                if (_issues is not null)
                {
                    // WHY: Match visited flags to the original node slots when reporting unreachable nodes.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var index = 0; index < _nodes.Length; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!visited[index])
                        {
                            _issues.Add(
                                NestedSetValidationCode.CycleOrUnreachableNode,
                                _nodes[index].Key,
                                "Adjacency contains a cycle or an unreachable node.");
                        }
                    }
                }
            }
        }

        /// <summary>Classifies repairable coordinate drift after all ambiguous adjacency errors are known.</summary>
        internal NestedSetInspection<TKey> Complete(
            List<NestedSetRepair<TKey>> repairs,
            CoordinateDifference differences,
            bool ordered
        )
        {
            // WHY: Gaps and corrupt derived coordinates are repairable; ambiguous adjacency must never be persisted.
            var canRebuild = _errors.Count == 0;
            if (canRebuild)
            {
                if ((differences & CoordinateDifference.Intervals) != 0)
                {
                    _errors.Add("Intervals do not match the ordered adjacency tree.");
                }

                if ((differences & CoordinateDifference.Depth) != 0)
                {
                    _errors.Add("Depths do not match the ordered adjacency tree.");
                }

                if ((differences & CoordinateDifference.Position) != 0)
                {
                    _errors.Add(
                        ordered
                            ? "Sibling positions do not match the configured ordering rule."
                            : "Sibling position is not dense and zero-based.");
                }
            }

            return new NestedSetInspection<TKey>(
                canRebuild,
                _nodes.LongLength,
                _errors,
                repairs,
                _issues?.Issues ?? [],
                _issues?.TreeIssues ?? [],
                _issues?.Counts ?? new Dictionary<NestedSetValidationCode, long>(),
                _issues?.TotalCount ?? 0);
        }
    }
}
