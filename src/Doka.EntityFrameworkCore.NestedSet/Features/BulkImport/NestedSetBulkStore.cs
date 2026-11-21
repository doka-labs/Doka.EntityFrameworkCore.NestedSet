namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

/// <summary>Persists generated-key parent links and refreshes only the imported scalar values.</summary>
/// <typeparam name="TEntity">The imported hierarchy entity.</typeparam>
/// <typeparam name="TKey">The mapped key type.</typeparam>
/// <typeparam name="TTreeId">The mapped tree identity type.</typeparam>
/// <typeparam name="TScope">The mapped scope type.</typeparam>
internal sealed partial class NestedSetBulkStore<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope> _batch;
    private readonly NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> _plan;
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly Dictionary<(int Count, bool Geometry), string> _templates = new();

    /// <summary>Shares metadata and parameter templates for one transaction-owned import.</summary>
    internal NestedSetBulkStore(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> plan
    )
    {
        _store = store;
        _plan = plan;
        _batch = new NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope>(store);
    }

    /// <summary>Writes only unresolved parents and changed geometry, combining them for native ordering.</summary>
    internal async Task FinalizeAsync(
        NestedSetParent<TKey> destinationParent,
        CancellationToken cancellationToken
    )
    {
        var geometry = _store.Map.Order is not null;
        var parameters = new List<NestedSetBatchParameter>(NestedSetBatch.MaximumRows * 6);
        var updated = 0;

        var nodeItems = _plan.Nodes;

        // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int nodeIndex = 0, nodeCount = nodeItems.Count; nodeIndex < nodeCount; nodeIndex++)
        {
            var node = nodeItems[nodeIndex];

            cancellationToken.ThrowIfCancellationRequested();

            if (!node.NeedsParentUpdate
                && node.Geometry == node.StagedGeometry)
            {
                continue;
            }

            var parent = node.Parent < 0
                ? destinationParent
                : new NestedSetParent<TKey>(true, _plan.Nodes[node.Parent].Key);

            parameters.Add(new NestedSetBatchParameter($"k{updated}", _store.Map.Key, node.Key));
            parameters.Add(new NestedSetBatchParameter($"p{updated}", _store.Map.Parent, parent.BoxedValue));

            if (geometry)
            {
                parameters.Add(new NestedSetBatchParameter($"l{updated}", _store.Map.Left, node.Geometry.Left));
                parameters.Add(new NestedSetBatchParameter($"r{updated}", _store.Map.Right, node.Geometry.Right));
                parameters.Add(new NestedSetBatchParameter($"d{updated}", _store.Map.Depth, node.Geometry.Depth));
                parameters.Add(new NestedSetBatchParameter($"o{updated}", _store.Map.Position, node.Geometry.Position));
            }

            updated++;

            if (updated == NestedSetBatch.MaximumRows)
            {
                await WriteAsync(updated, geometry, parameters, cancellationToken)
                    .ConfigureAwait(false);

                parameters.Clear();
                updated = 0;
            }
        }

        if (updated != 0)
        {
            await WriteAsync(updated, geometry, parameters, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Reuses one flat parameterized template per batch size without retaining entity payload.</summary>
    private async Task WriteAsync(
        int count,
        bool geometry,
        IReadOnlyList<NestedSetBatchParameter> parameters,
        CancellationToken cancellationToken
    )
    {
        if (!_templates.TryGetValue((count, geometry), out var sql))
        {
            sql = FinalizationTemplate(count, geometry);
            _templates.Add((count, geometry), sql);
        }

        // WHY: Configured sort payload must remain stable during structural writes, including database triggers
        // (docs/ordering.md). Native ranks therefore precede this combined parent/geometry update safely.
        await _batch
            .ExecuteAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads native ordering only from the reserved import interval, including generated sort values.
    /// </summary>
    internal async Task<IReadOnlyDictionary<TKey, int>?> ReadRanksAsync(
        long boundary,
        CancellationToken cancellationToken
    )
    {
        if (_store.Map.Order is not { } order)
        {
            return null;
        }

        var end = checked(boundary + _plan.Width - 1);
        var imported = _store.Nodes.Where(node => EF.Property<long>(node, _store.Map.Left) >= boundary
            && EF.Property<long>(node, _store.Map.Right) <= end);

        var ranks = new Dictionary<TKey, int>(_plan.Nodes.Count, _store.Map.KeyComparer);

        // WHY: Database collation, null placement, converters and generated criteria must define sibling order.
        // The global rank is used only within each input sibling group, never to flatten hierarchy levels.
        await foreach (var key in order
            .Apply(imported)
            .Select(_store.Property<TKey>(_store.Map.Key))
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            ranks.Add(key, ranks.Count);
        }

        return ranks;
    }

    /// <summary>Builds bounded CASE assignments using mapped key, parent and coordinate parameters.</summary>
    private string FinalizationTemplate(
        int count,
        bool geometry
    )
    {
        var key = _batch.Column(_store.Map.Key);
        var sql = new StringBuilder($"UPDATE {_batch.Table} SET ");
        AppendAssignment(sql, _store.Map.Parent, "p", count);

        if (geometry)
        {
            foreach (var (property, prefix) in new[]
                {
                    (_store.Map.Left, "l"),
                    (_store.Map.Right, "r"),
                    (_store.Map.Depth, "d"),
                    (_store.Map.Position, "o"),
                })
            {
                sql.Append(", ");
                AppendAssignment(sql, property, prefix, count);
            }
        }

        sql.Append(CultureInfo.InvariantCulture, $" WHERE {_batch.KeyBatchIdentityPredicate} AND {key} IN (");

        for (var index = 0; index < count; index++)
        {
            if (index != 0)
            {
                sql.Append(", ");
            }

            sql.Append(_batch.Parameter($"k{index}"));
        }

        sql.Append(')');

        return sql.ToString();
    }

    /// <summary>Appends one column's flat CASE without allocating nested expression trees.</summary>
    private void AppendAssignment(
        StringBuilder sql,
        string property,
        string prefix,
        int count
    )
    {
        var column = _batch.Column(property);
        sql.Append(
            CultureInfo.InvariantCulture,
            $"{column} = CASE {_batch.Column(_store.Map.Key)}");

        for (var index = 0; index < count; index++)
        {
            sql.Append(
                CultureInfo.InvariantCulture,
                $" WHEN {_batch.Parameter($"k{index}")} THEN {_batch.Parameter($"{prefix}{index}")}");
        }

        sql.Append(CultureInfo.InvariantCulture, $" ELSE {column} END");
    }
}
