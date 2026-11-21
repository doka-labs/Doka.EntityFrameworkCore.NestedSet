namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal sealed partial class NestedSetSaveGroup<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Accumulates authoritative write intervals from all sequential reorder operations.</summary>
    private void AddIntervals(
        TScope scope,
        TTreeId treeId,
        IReadOnlyList<NestedSetChangedInterval> intervals
    )
    {
        if (intervals.Count == 0)
        {
            return;
        }

        var tree = _changedIntervals.FirstOrDefault(candidate =>
            _scopes.Comparer.Equals(candidate.Scope, scope) && _treeIdComparer.Equals(candidate.TreeId, treeId));

        if (tree is null)
        {
            tree = new TreeIntervals(scope, treeId);
            _changedIntervals.Add(tree);
        }

        tree.Intervals.AddRange(intervals);
    }

    /// <summary>Filters persisted coordinates; tracked bounds can be stale and cannot define membership.</summary>
    private IEnumerable<Expression<Func<TEntity, bool>>> AffectedIntervals()
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");
        var clauses = new List<Expression>(NestedSetBatch.MaximumRows);
        // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int treeIndex = 0, treeCount = _changedIntervals.Count; treeIndex < treeCount; treeIndex++)
        {
            var tree = _changedIntervals[treeIndex];

            // WHY: Coalescing overlapping spans keeps parameter and expression size proportional to distinct
            // changed regions, while separate stable islands remain outside the refresh projection.
            var ordered = tree
                .Intervals
                .OrderBy(interval => interval.First)
                .ThenBy(interval => interval.Last)
                .ToArray();

            var first = ordered[0].First;
            var last = ordered[0].Last;
            for (var index = 1; index <= ordered.Length; index++)
            {
                if (index < ordered.Length
                    && (last == long.MaxValue || ordered[index].First <= checked(last + 1)))
                {
                    last = Math.Max(last, ordered[index].Last);

                    continue;
                }

                var lower = first;
                var upper = last;
                Expression<Func<TEntity, bool>> range = node =>
                    (EF.Property<long>(node, _map.Left) >= lower && EF.Property<long>(node, _map.Left) <= upper)
                    || (EF.Property<long>(node, _map.Right) >= lower && EF.Property<long>(node, _map.Right) <= upper);

                var treeFilter = NestedSetKeyFilter<TEntity>.Matches(_map.TreeIdProperty, new[] { tree.TreeId });

                var treeRange = new ParameterReplacer(range.Parameters[0], treeFilter.Parameters[0]).Visit(range.Body);

                Expression clause = Expression.AndAlso(treeFilter.Body, treeRange);

                if (_map.Scope is not null)
                {
                    var scoped = NestedSetKeyFilter<TEntity>.Matches(_map.ScopeProperty!, [tree.Scope]);
                    var treeClause = new ParameterReplacer(treeFilter.Parameters[0], scoped.Parameters[0])
                        .Visit(clause);

                    clause = Expression.AndAlso(scoped.Body, treeClause);
                    clause = new ParameterReplacer(scoped.Parameters[0], parameter).Visit(clause);
                }
                else
                {
                    clause = new ParameterReplacer(treeFilter.Parameters[0], parameter).Visit(clause);
                }

                clauses.Add(clause);

                if (clauses.Count == NestedSetBatch.MaximumRows)
                {
                    // WHY: Disjoint edited regions also consume SQL parameters. Batching only tracker keys
                    // would still exceed provider limits for a save touching thousands of independent regions.
                    yield return Expression.Lambda<Func<TEntity, bool>>(Combine(clauses, 0, clauses.Count), parameter);
                    clauses.Clear();
                }

                if (index < ordered.Length)
                {
                    first = ordered[index].First;
                    last = ordered[index].Last;
                }
            }
        }

        if (clauses.Count > 0)
        {
            yield return Expression.Lambda<Func<TEntity, bool>>(Combine(clauses, 0, clauses.Count), parameter);
        }
    }

    /// <summary>Keeps predicate nesting logarithmic when combining independent changed regions.</summary>
    private static Expression Combine(
        IReadOnlyList<Expression> clauses,
        int start,
        int count
    )
    {
        if (count == 1)
        {
            return clauses[start];
        }

        var half = count / 2;

        return Expression.OrElse(
            Combine(clauses, start, half),
            Combine(clauses, start + half, count - half));
    }
}
