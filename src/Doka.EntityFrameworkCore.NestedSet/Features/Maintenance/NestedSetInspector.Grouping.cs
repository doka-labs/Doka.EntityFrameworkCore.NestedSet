namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

internal static partial class NestedSetInspector<TKey>
    where TKey : notnull
{
    /// <summary>Owns the buffers shared by one inspection's grouping, traversal, and diagnostics.</summary>
    private readonly partial struct InspectionRun
    {
        private readonly NestedSetNode<TKey>[] _nodes;
        private readonly Dictionary<TKey, TKey>? _parents;
        private readonly Dictionary<TKey, List<int>> _children;
        private readonly List<int> _roots;
        private readonly List<string> _errors;
        private readonly NestedSetIssueCollector<TKey>? _issues;
        private readonly HashSet<TKey>? _keys;

        /// <summary>Retains the structural snapshot and allocates only its adjacency and diagnostic buffers.</summary>
        internal InspectionRun(
            NestedSetNode<TKey>[] snapshot,
            Dictionary<TKey, TKey>? canonicalParents,
            IEqualityComparer<TKey> keyComparer,
            bool collectIssues,
            int issueLimit
        )
        {
            _nodes = snapshot;
            _parents = canonicalParents;
            _children = new Dictionary<TKey, List<int>>(keyComparer);
            _roots = [];
            _errors = [];
            _issues = collectIssues
                ? new NestedSetIssueCollector<TKey>(issueLimit)
                : null;

            _keys = canonicalParents is null
                ? new HashSet<TKey>(snapshot.Select(node => node.Key), keyComparer)
                : null;
        }

        /// <summary>Indexes existing node slots by their canonical parent while retaining adjacency errors.</summary>
        internal void Group(
            bool ordered,
            CancellationToken cancellationToken
        )
        {
            // WHY: Store root and child ordinals that reference the existing structural snapshot directly.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _nodes.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var node = _nodes[index];
                if (!ordered
                    && node.Position < 0)
                {
                    AddIssue(
                        _errors,
                        _issues,
                        NestedSetValidationCode.NegativePosition,
                        node.Key,
                        "Sibling position must be non-negative.");
                }

                if (!node.Parent.HasValue)
                {
                    _roots.Add(index);
                }
                else if (ResolveParent(node, _parents, _keys, out var parent))
                {
                    if (!_children.TryGetValue(parent, out var siblings))
                    {
                        // WHY: Deep chains have one child per parent; reserving one slot avoids unused list capacity.
                        siblings = new List<int>(1);
                        _children.Add(parent, siblings);
                    }

                    siblings.Add(index);
                }
                else
                {
                    AddIssue(
                        _errors,
                        _issues,
                        NestedSetValidationCode.MissingParent,
                        node.Key,
                        "A parent is missing from the node's tree.");
                }
            }
        }

        /// <summary>Validates root cardinality and, without configured ordering, canonical sibling positions.</summary>
        internal void ValidateOrder(
            bool ordered,
            CancellationToken cancellationToken
        )
        {
            // WHY: Capture the existing snapshot, not this value context, in the shared sibling comparer.
            var nodes = _nodes;
            var siblingComparer = Comparer<int>.Create((first, second) =>
                nodes[first].Position.CompareTo(nodes[second].Position));

            if (!ordered)
            {
                foreach (var siblings in _children.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SortSiblings(_nodes, siblings, siblingComparer, _errors, _issues);
                }

                SortSiblings(_nodes, _roots, siblingComparer, _errors, _issues);
            }

            if (_roots.Count != 1)
            {
                const string message = "A TreeId must contain exactly one root.";
                _errors.Add(message);
                _issues?.AddTree(NestedSetValidationCode.InvalidRootCount, message);

                if (_issues is not null
                    && _roots.Count > 1)
                {
                    // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (int rootIndex = 0, rootCount = _roots.Count; rootIndex < rootCount; rootIndex++)
                    {
                        var root = _roots[rootIndex];

                        _issues.Add(NestedSetValidationCode.InvalidRootCount, _nodes[root].Key, message);
                    }
                }
            }
        }
    }
}
