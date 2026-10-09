namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

/// <summary>Writes structural plans using cached flat CASE templates and bounded mapped parameters.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TScope">The scope key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
internal sealed class NestedSetRepairWriter<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope> _batch;
    private readonly Dictionary<int, string> _templates = new();

    /// <summary>Shares one metadata and template cache across every batch in an operation.</summary>
    internal NestedSetRepairWriter(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store
    )
    {
        _store = store;
        _batch = new NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope>(store);
    }

    /// <summary>Writes at most the shared batch limit without recompiling an EF expression per batch.</summary>
    /// <param name="repairs">The complete structural plan.</param>
    /// <param name="offset">The first row to write.</param>
    /// <param name="cancellationToken">The command cancellation token.</param>
    /// <returns>The affected row count.</returns>
    internal Task<int> WriteAsync(
        IReadOnlyList<NestedSetRepair<TKey>> repairs,
        int offset,
        CancellationToken cancellationToken
    )
    {
        var count = Math.Min(NestedSetBatch.MaximumRows, repairs.Count - offset);
        if (count <= 0)
        {
            return Task.FromResult(0);
        }

        if (!_templates.TryGetValue(count, out var sql))
        {
            sql = CreateTemplate(count);
            _templates.Add(count, sql);
        }

        var values = new NestedSetBatchParameter[count * 5];
        for (var index = 0; index < count; index++)
        {
            var repair = repairs[offset + index];
            var slot = index * 5;
            values[slot] = new NestedSetBatchParameter($"k{index}", _store.Map.Key, repair.Key);
            values[slot + 1] = new NestedSetBatchParameter($"l{index}", _store.Map.Left, repair.Left);
            values[slot + 2] = new NestedSetBatchParameter($"r{index}", _store.Map.Right, repair.Right);
            values[slot + 3] = new NestedSetBatchParameter($"d{index}", _store.Map.Depth, repair.Depth);
            values[slot + 4] = new NestedSetBatchParameter($"p{index}", _store.Map.Position, repair.Position);
        }

        return _batch.ExecuteAsync(sql, values, cancellationToken);
    }

    /// <summary>Creates flat CASE expressions whose immutable-key predicates ignore assignment order.</summary>
    private string CreateTemplate(
        int count
    )
    {
        var key = _batch.Column(_store.Map.Key);
        var sql = new StringBuilder($"UPDATE {_batch.Table} SET ");
        AppendCoordinate(sql, key, _store.Map.Left, "l", count);
        sql.Append(", ");
        AppendCoordinate(sql, key, _store.Map.Right, "r", count);
        sql.Append(", ");
        AppendCoordinate(sql, key, _store.Map.Depth, "d", count);
        sql.Append(", ");
        AppendCoordinate(sql, key, _store.Map.Position, "p", count);
        sql.Append(
            CultureInfo.InvariantCulture,
            $" WHERE {_batch.KeyBatchIdentityPredicate} AND {key} IN (");

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

    /// <summary>Appends a flat CASE, avoiding SQL Server's ten-level nested-CASE limit.</summary>
    private void AppendCoordinate(
        StringBuilder sql,
        string key,
        string property,
        string prefix,
        int count
    )
    {
        var column = _batch.Column(property);
        sql.Append(CultureInfo.InvariantCulture, $"{column} = CASE {key}");
        for (var index = 0; index < count; index++)
        {
            sql.Append(
                CultureInfo.InvariantCulture,
                $" WHEN {_batch.Parameter($"k{index}")} THEN {_batch.Parameter($"{prefix}{index}")}");
        }

        // WHY: One CASE with many WHEN arms has constant nesting depth and uses only 5 * 64 + 1 parameters.
        // Both the discriminator and the scope predicate retain native database key and collation semantics.
        sql.Append(CultureInfo.InvariantCulture, $" ELSE {column} END");
    }
}
