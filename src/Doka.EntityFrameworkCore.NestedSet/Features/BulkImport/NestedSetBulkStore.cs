namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

/// <summary>Persists generated-key parent links and refreshes only the imported scalar values.</summary>
/// <typeparam name="TEntity">The imported hierarchy entity.</typeparam>
/// <typeparam name="TKey">The mapped key type.</typeparam>
/// <typeparam name="TTreeId">The mapped tree identity type.</typeparam>
/// <typeparam name="TScope">The mapped scope type.</typeparam>
internal sealed class NestedSetBulkStore<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> _plan;
    private readonly NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope> _batch;
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

        foreach (var node in _plan.Nodes)
        {
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
                await WriteAsync(updated, geometry, parameters, cancellationToken).ConfigureAwait(false);
                parameters.Clear();
                updated = 0;
            }
        }

        if (updated != 0)
        {
            await WriteAsync(updated, geometry, parameters, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    ///     Returns final coordinates and update-generated values without materializing existing forest payload.
    /// </summary>
    /// <param name="boundary">The beginning of the reserved import interval before optional native ordering.</param>
    /// <param name="cancellationToken">The token checked while capturing identities and streaming final values.</param>
    internal async Task RefreshAsync(
        long boundary,
        CancellationToken cancellationToken
    )
    {
        var properties = new[]
            {
                _store.Map.KeyProperty,
                _store.Map.LeftProperty,
                _store.Map.RightProperty,
                _store.Map.DepthProperty,
                _store.Map.PositionProperty,
                _store.Map.ParentProperty,
            }
            .Concat(
                _store
                    .Map
                    .EntityType
                    .GetFlattenedProperties()
                    .Where(property => (property.ValueGenerated & ValueGenerated.OnUpdate) != 0))
            .Distinct()
            .ToArray();

        // WHY: Refresh applies arbitrary provider-generated and complex scalar metadata values. This heterogeneous
        // row exists only at the materialization/metadata-assignment boundary; expected identities remain typed.
        var parameter = Expression.Parameter(typeof(TEntity), "node");
        var projection = Expression.Lambda<Func<TEntity, object[]>>(
            Expression.NewArrayInit(
                typeof(object),
                properties.Select(property => Expression.Convert(
                    NestedSetExpressions.Property(parameter, property),
                    typeof(object)))),
            parameter);

        var keys = CaptureExpectedKeys(cancellationToken);

        if (_store.Map.Order is null)
        {
            await RefreshIntervalAsync(boundary, properties, projection, keys, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // WHY: Native ordering can place existing sibling subtrees between imported nodes. Their final rows no
        // longer share the reserved interval, so this path keeps its bounded, exact-tree key selection.
        for (var offset = 0; offset < _plan.Nodes.Count; offset += NestedSetBatch.MaximumRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(NestedSetBatch.MaximumRows, _plan.Nodes.Count - offset);
            var entries = new Dictionary<TKey, int>(count, _store.Map.KeyComparer);

            for (var index = offset; index < offset + count; index++)
            {
                entries.Add(keys[index], index);
            }

            var query = _store
                .Nodes
                .Where(NestedSetKeyFilter<TEntity>.Matches(_store.Map.KeyProperty, entries.Keys.ToArray()))
                .TagWith(NestedSetDiagnostics.BulkRefreshTag);

            var refreshed = 0;

            await foreach (var values in query
                               .Select(projection)
                               .AsAsyncEnumerable()
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                var key = (TKey)values[0];

                // WHY: An EF key comparer may equate two different stored values. The dictionary only locates
                // the candidate; the provider comparison authorizes its structural refresh.
                if (!entries.TryGetValue(key, out var index)
                    || !NestedSetTypedValue<TKey>.Matches(_store.Map.KeyProperty, key, keys[index]))
                {
                    throw new DbUpdateConcurrencyException("The imported refresh contains an unexpected node.");
                }

                refreshed++;

                RefreshEntry(_plan.Nodes[index], properties, values);
            }

            if (refreshed != count)
            {
                throw new DbUpdateConcurrencyException("An imported node disappeared from its tree during insertion.");
            }
        }

        VerifyInputKeys(keys, cancellationToken);
    }

    /// <summary>Captures stable key representations before any final refresh reader can mutate an input.</summary>
    private TKey[] CaptureExpectedKeys(
        CancellationToken cancellationToken
    )
    {
        var keys = new TKey[_plan.Nodes.Count];

        for (var index = 0; index < keys.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: Application key comparers can equate database-distinct values, and a reader callback can
            // mutate an input's array key. Independent snapshots preserve the expected membership.
            keys[index] = NestedSetTypedValue<TKey>.Snapshot(_store.Map.KeyProperty, _plan.Nodes[index].Key);
        }

        return keys;
    }

    /// <summary>Streams the unordered import's reserved interval and verifies every planned row exactly once.</summary>
    private async Task RefreshIntervalAsync(
        long boundary,
        IProperty[] properties,
        Expression<Func<TEntity, object[]>> projection,
        TKey[] keys,
        CancellationToken cancellationToken
    )
    {
        var end = checked(boundary + _plan.Width - 1);
        var imported = _store
            .Nodes
            .Where(node => EF.Property<long>(node, _store.Map.Left) >= boundary
                && EF.Property<long>(node, _store.Map.Right) <= end)
            .TagWith(NestedSetDiagnostics.BulkRefreshTag)
            .OrderBy(_store.Property<long>(_store.Map.Left));

        var refreshed = 0;

        // WHY: Explicit placement retains the plan's preorder inside one contiguous interval. The TreeId/Left
        // index streams that same order, avoiding both per-batch reads and a second entry dictionary per node.
        await foreach (var values in imported
                           .Select(projection)
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (refreshed == keys.Length
                || !NestedSetTypedValue<TKey>.Matches(_store.Map.KeyProperty, (TKey)values[0], keys[refreshed]))
            {
                throw new DbUpdateConcurrencyException("The imported interval contains an unexpected node.");
            }

            var node = _plan.Nodes[refreshed];
            var geometry = node.Geometry;

            if ((long)values[1] != geometry.Left
                || (long)values[2] != geometry.Right
                || (int)values[3] != geometry.Depth
                || (long)values[4] != geometry.Position)
            {
                throw new DbUpdateConcurrencyException(
                    "An imported node changed its planned geometry during insertion.");
            }

            RefreshEntry(node, properties, values);
            refreshed++;
        }

        if (refreshed != keys.Length)
        {
            throw new DbUpdateConcurrencyException("An imported node disappeared from its reserved interval.");
        }

        VerifyInputKeys(keys, cancellationToken);
    }

    /// <summary>Rejects input keys changed by callbacks or mapped setters during either refresh path.</summary>
    private void VerifyInputKeys(
        TKey[] keys,
        CancellationToken cancellationToken
    )
    {
        // WHY: Reader callbacks or mapped setters can mutate an input key while its physical row stays intact,
        // including an earlier input changed by a later setter. Read the caller-visible value after disposal and
        // compare it with the independent database-membership snapshot.
        for (var index = 0; index < keys.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = NestedSetTypedValue<TKey>.Read(_plan.Nodes[index].Entity, _store.Map.KeyProperty);

            if (!NestedSetTypedValue<TKey>.Matches(_store.Map.KeyProperty, current, keys[index]))
            {
                throw new DbUpdateConcurrencyException("An imported input changed its identity during refresh.");
            }
        }
    }

    /// <summary>Applies scalar and complex values through one short-lived detached entry handle.</summary>
    private void RefreshEntry(
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>.Node node,
        IProperty[] properties,
        object[] values
    )
    {
        var entry = _store.Entry(node.Entity);

        try
        {
            // WHY: CurrentValues is a live public EF view, but each access creates a wrapper. Reuse one view
            // while applying this row's heterogeneous metadata values without adding an internal EF seam.
            var current = entry.CurrentValues;

            for (var index = 1; index < properties.Length; index++)
            {
                // WHY: SQL parent/coordinate updates can change row versions after INSERT returned them. A detached
                // root entry applies mapped CLR values, including complex leaves, without relationship fixup.
                current[properties[index]] = values[index];
            }
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
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

        sql.Append(
            CultureInfo.InvariantCulture,
            $" WHERE {_batch.IdentityPredicate} AND {key} IN (");

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
