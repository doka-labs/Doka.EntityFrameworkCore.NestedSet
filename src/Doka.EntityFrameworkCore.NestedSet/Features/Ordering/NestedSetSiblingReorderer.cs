namespace Doka.EntityFrameworkCore.NestedSet.Features.Ordering;

/// <summary>Permutes sibling intervals in bounded batches without materializing their descendant rows.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The optional mapped scope type or the scope-free marker.</typeparam>
internal sealed class NestedSetSiblingReorderer<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope> _batch;
    private readonly Dictionary<(int Count, bool Mark), string> _templates = new();

    /// <summary>Uses the transaction and write lock already held by the calling mutation.</summary>
    /// <param name="store">The tree-bound store with a configured ordering rule.</param>
    internal NestedSetSiblingReorderer(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store
    )
    {
        _store = store;
        _batch = new NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope>(store);
    }

    /// <summary>Restores canonical sibling order with memory proportional to direct siblings only.</summary>
    /// <param name="parent">The typed parent, or an absent value for the root group.</param>
    /// <param name="cancellationToken">The token used for planning and batched writes.</param>
    /// <returns>The coordinate ranges containing rows changed by the permutation.</returns>
    internal async Task<IReadOnlyList<NestedSetChangedInterval>> ReorderAsync(
        NestedSetParent<TKey> parent,
        CancellationToken cancellationToken
    )
    {
        var order = _store.Map.Order ?? throw new InvalidOperationException("A sibling ordering rule is required.");

        var siblings = await order
            .Apply(_store.Siblings(parent))
            .Select(_store.Map.Projection)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ReorderAsync(siblings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies an already resolved sibling order, including bulk-import placement plans.</summary>
    /// <param name="siblings">Every sibling in the group, in its required final order.</param>
    /// <param name="cancellationToken">The token used for planning and bounded writes.</param>
    /// <returns>The old and new ranges affected by writes; empty when the group is already ordered.</returns>
    internal async Task<IReadOnlyList<NestedSetChangedInterval>> ReorderAsync(
        IReadOnlyList<NestedSetNode<TKey>> siblings,
        CancellationToken cancellationToken
    )
    {
        if (siblings.Count == 0)
        {
            return [];
        }

        var next = siblings.Min(node => node.Left);
        var moved = new List<(NestedSetNode<TKey> Node, long Delta, long Position)>();
        var positions = new List<(long Left, long Position)>();
        var intervals = new List<NestedSetChangedInterval>();

        for (var index = 0; index < siblings.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sibling = siblings[index];
            NestedSetGuards.RequireBounds(sibling);
            var delta = checked(next - sibling.Left);

            if (delta != 0)
            {
                moved.Add((sibling, delta, index));
                intervals.Add(new NestedSetChangedInterval(sibling.Left, sibling.Right));
                intervals.Add(new NestedSetChangedInterval(next, checked(sibling.Right + delta)));
            }
            else if (sibling.Position != index)
            {
                // WHY: Unequal subtree widths can preserve coordinates while changing the root's ordinal.
                // Descendants in this case need neither coordinate updates nor refreshed concurrency tokens.
                positions.Add((sibling.Left, index));
                intervals.Add(new NestedSetChangedInterval(sibling.Left, sibling.Left));
            }

            next = checked(next + checked(sibling.Right - sibling.Left + 1));
        }

        var stagingOffset = 0L;
        if (moved.Count > 0)
        {
            var maximumRight = await _store
                .Nodes
                .MaxAsync(_store.Property<long>(_store.Map.Right), cancellationToken)
                .ConfigureAwait(false);

            var minimumMovedLeft = moved.Min(item => item.Node.Left);
            var maximumMovedRight = moved.Max(item => item.Node.Right);
            stagingOffset = checked(maximumRight - minimumMovedLeft + 1);

            // WHY: Staging above every live boundary prevents later batches from selecting rows already moved.
            // Checked endpoint arithmetic fails before the first write if the persisted tree has exhausted Int64.
            _ = checked(maximumMovedRight + stagingOffset);
        }

        // WHY: Every moved interval is staged before any is restored. Later batches can otherwise mistake restored
        // coordinates for a different source interval. Positive staging keeps every database check valid throughout.
        foreach (var mark in new[] { true, false })
        {
            for (var offset = 0; offset < moved.Count; offset += NestedSetBatch.MaximumRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(NestedSetBatch.MaximumRows, moved.Count - offset);
                if (!_templates.TryGetValue((count, mark), out var sql))
                {
                    sql = mark ? CreateMarkTemplate(count) : CreateTemplate(count);
                    _templates.Add((count, mark), sql);
                }

                var stride = mark ? 2 : 4;
                var values = new NestedSetBatchParameter[(count * stride) + 3];
                var minimumRight = long.MaxValue;
                var maximumRight = long.MinValue;

                for (var index = 0; index < count; index++)
                {
                    var (node, delta, position) = moved[offset + index];
                    var first = mark ? node.Left : checked(node.Left + stagingOffset);
                    var last = mark ? node.Right : checked(node.Right + stagingOffset);
                    minimumRight = Math.Min(minimumRight, first);
                    maximumRight = Math.Max(maximumRight, last);
                    values[index * stride] = new NestedSetBatchParameter($"a{index}", _store.Map.Right, first);
                    values[(index * stride) + 1] = new NestedSetBatchParameter($"b{index}", _store.Map.Right, last);

                    if (!mark)
                    {
                        values[(index * stride) + 2] = new NestedSetBatchParameter($"d{index}", _store.Map.Left, delta);
                        values[(index * stride) + 3] = new NestedSetBatchParameter($"p{index}", _store.Map.Position, position);
                    }
                }

                // WHY: The envelope exposes a bounded Right-index seek even when the optimizer treats the
                // disjoint OR as a residual filter. Keeping the exact intervals leaves intervening subtrees untouched.
                values[^3] = new NestedSetBatchParameter("m", _store.Map.Left, stagingOffset);
                values[^2] = new NestedSetBatchParameter("low", _store.Map.Right, minimumRight);
                values[^1] = new NestedSetBatchParameter("high", _store.Map.Right, maximumRight);

                await _batch
                    .ExecuteAsync(sql, values, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        for (var offset = 0; offset < positions.Count; offset += NestedSetBatch.MaximumRows)
        {
            var count = Math.Min(NestedSetBatch.MaximumRows, positions.Count - offset);
            var values = new NestedSetBatchParameter[count * 2];
            var left = _batch.Column(_store.Map.Left);
            var position = _batch.Column(_store.Map.Position);
            var assignments = new StringBuilder($"CASE {left}");
            var filter = new string[count];

            for (var index = 0; index < count; index++)
            {
                var item = positions[offset + index];
                values[index * 2] = new NestedSetBatchParameter($"l{index}", _store.Map.Left, item.Left);
                values[(index * 2) + 1] = new NestedSetBatchParameter($"p{index}", _store.Map.Position, item.Position);
                assignments.Append(
                    CultureInfo.InvariantCulture,
                    $" WHEN {_batch.Parameter($"l{index}")} THEN {_batch.Parameter($"p{index}")}");

                filter[index] = _batch.Parameter($"l{index}");
            }

            assignments.Append(CultureInfo.InvariantCulture, $" ELSE {position} END");
            var sql = $"UPDATE {_batch.Table} SET {position} = {assignments}"
                + $" WHERE {_batch.IdentityPredicate}"
                + $" AND {left} IN ({string.Join(", ", filter)})";

            await _batch
                .ExecuteAsync(sql, values, cancellationToken)
                .ConfigureAwait(false);
        }

        return MergeIntervals(intervals);
    }

    /// <summary>Marks only changed source intervals, without touching unchanged trees between them.</summary>
    private string CreateMarkTemplate(
        int count
    )
    {
        var left = _batch.Column(_store.Map.Left);
        var right = _batch.Column(_store.Map.Right);
        var filter = new string[count];

        for (var index = 0; index < count; index++)
        {
            filter[index] = $"({right} BETWEEN {_batch.Parameter($"a{index}")} AND {_batch.Parameter($"b{index}")})";
        }

        return $"UPDATE {_batch.Table} SET {left} = {left} + {_batch.Parameter("m")}, "
            + $"{right} = {right} + {_batch.Parameter("m")}"
            + $" WHERE {_batch.IdentityPredicate}"
            + $" AND {right} BETWEEN {_batch.Parameter("low")} AND {_batch.Parameter("high")}"
            + $" AND ({string.Join(" OR ", filter)})";
    }

    /// <summary>Coalesces overlapping effects while preserving gaps occupied by unchanged subtrees.</summary>
    private static List<NestedSetChangedInterval> MergeIntervals(
        List<NestedSetChangedInterval> intervals
    )
    {
        if (intervals.Count == 0)
        {
            return [];
        }

        intervals.Sort((first, second) => first.First.CompareTo(second.First));
        var merged = new List<NestedSetChangedInterval>();
        var current = intervals[0];

        for (var index = 1; index < intervals.Count; index++)
        {
            var next = intervals[index];
            if (current.Last == long.MaxValue
                || next.First <= checked(current.Last + 1))
            {
                current = new(current.First, Math.Max(current.Last, next.Last));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);

        return merged;
    }

    /// <summary>Builds flat CASE expressions with the stable right boundary assigned last.</summary>
    private string CreateTemplate(
        int count
    )
    {
        var left = _batch.Column(_store.Map.Left);
        var right = _batch.Column(_store.Map.Right);
        var position = _batch.Column(_store.Map.Position);
        var delta = new StringBuilder("CASE");
        var positions = new StringBuilder($"CASE {right}");
        var filter = new StringBuilder();
        for (var index = 0; index < count; index++)
        {
            var contains = $"{right} BETWEEN {_batch.Parameter($"a{index}")} AND {_batch.Parameter($"b{index}")}";
            delta.Append(
                CultureInfo.InvariantCulture,
                $" WHEN {contains} THEN {_batch.Parameter($"d{index}")}");

            positions.Append(
                CultureInfo.InvariantCulture,
                $" WHEN {_batch.Parameter($"b{index}")} THEN {_batch.Parameter($"p{index}")}");

            if (index > 0)
            {
                filter.Append(" OR ");
            }

            filter
                .Append('(')
                .Append(contains)
                .Append(')');
        }

        delta.Append(" ELSE 0 END");
        positions.Append(CultureInfo.InvariantCulture, $" ELSE {position} END");

        // WHY: MySQL evaluates setters in order. Every condition reads the staged Right discriminator; updating it
        // last allows one command per batch while preserving the staged coordinate for every CASE expression.
        return $"UPDATE {_batch.Table} SET {position} = {positions}, "
            + $"{left} = {left} - {_batch.Parameter("m")} + {delta}, "
            + $"{right} = {right} - {_batch.Parameter("m")} + {delta}"
            + $" WHERE {_batch.IdentityPredicate}"
            + $" AND {right} BETWEEN {_batch.Parameter("low")} AND {_batch.Parameter("high")}"
            + $" AND ({filter})";
    }
}
