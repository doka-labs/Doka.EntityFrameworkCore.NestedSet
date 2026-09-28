namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal sealed partial class NestedSetSaveGroup<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <inheritdoc />
    internal override async Task RefreshAsync(
        CancellationToken cancellationToken
    )
    {
        if (_changedIntervals.Count == 0
            || _tracked.Length == 0)
        {
            return;
        }

        var capabilities = NestedSetProviderCapabilities.Resolve(_context);
        var nativeKeys = _map.HasNativeKeyEquality && capabilities.SupportsTrackedKeyCollection(_map.KeyProperty);

        // WHY: A CLR scope lookup is exact only under native scope equality. A NodeKey that is an EF key by
        // itself needs no scope lookup; otherwise a tracked scope alias must be resolved by the database.
        if (nativeKeys && (_map.Scope is null || _map.HasUniqueNodeKey || _map.HasNativeScopeEquality))
        {
            await RefreshNativeAsync(capabilities, cancellationToken).ConfigureAwait(false);

            return;
        }

        var source = Nodes();

        if (_map.Scope is null)
        {
            await RefreshScopelessAsync(source, cancellationToken).ConfigureAwait(false);

            return;
        }

        await RefreshScopedAsync(source, nativeKeys ? capabilities : null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refreshes tracked rows whose scope equality must be resolved by the database.</summary>
    /// <param name="source">The hierarchy set with application filters bypassed.</param>
    /// <param name="nativeKeys">The verified native key transport, or null when keys need ordinal rowsets.</param>
    /// <param name="cancellationToken">The token for bounded scope and identity queries.</param>
    private async Task RefreshScopedAsync(
        IQueryable<TEntity> source,
        NestedSetProviderCapabilities? nativeKeys,
        CancellationToken cancellationToken
    )
    {
        // WHY: Scope aliases are resolved in SQL once per distinct tracked value. A CLR scope comparison would
        // either miss database-equal aliases or retain thousands of foreign entries for pointless key batches.
        var scopes = _tracked
            .GroupBy(entry => NestedSetTypedValue<TScope>.Read(entry, _map.ScopeProperty!), _scopes.Comparer)
            .ToArray();

        foreach (var filter in AffectedIntervals())
        {
            var affected = source.Where(filter);
            var rowFilter = RowFilter(filter);

            foreach (var scopeBatch in scopes.Chunk(NestedSetBatch.MaximumRows))
            {
                var matching = await NestedSetTrackedRowset<TEntity>
                    .ExistingScopes<TScope, TKey>(
                        affected,
                        _map.Scope!,
                        _map.Key,
                        scopeBatch
                            .Select(group => group.Key)
                            .ToArray())
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (nativeKeys is not null)
                {
                    foreach (var index in matching)
                    {
                        await RefreshScopeNativeAsync(affected, nativeKeys, scopeBatch[index], cancellationToken)
                            .ConfigureAwait(false);
                    }

                    continue;
                }

                foreach (var batch in matching
                             .SelectMany(index => scopeBatch[index])
                             .Chunk(NestedSetBatch.MaximumRows))
                {
                    var scopeValues = new TScope[batch.Length];
                    var keys = new TKey[batch.Length];

                    for (var index = 0; index < batch.Length; index++)
                    {
                        var values = batch[index].CurrentValues;
                        scopeValues[index] = NestedSetTypedValue<TScope>.Read(values, _map.ScopeProperty!);
                        keys[index] = NestedSetTypedValue<TKey>.Read(values, _map.KeyProperty);
                    }

                    // WHY: NodeKey can repeat in another Scope, so every branch matches both identity parts.
                    // The authoritative interval predicate is applied once outside UNION ALL.
                    var rows = await NestedSetTrackedRowset<TEntity>
                        .MatchScoped(source, _map.Scope!, _map.Key, scopeValues, keys)
                        .Where(rowFilter)
                        .Select(_refreshProjection)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var row in rows)
                    {
                        RefreshEntry(batch[(int)row[0]], row);
                    }
                }
            }
        }
    }

    /// <summary>Refreshes one tracked scope through native key transport after SQL resolved its scope.</summary>
    /// <param name="affected">The persisted rows inside the current batch of changed intervals.</param>
    /// <param name="capabilities">The verified provider key transport.</param>
    /// <param name="scope">The tracked entries sharing one exact CLR scope representation.</param>
    /// <param name="cancellationToken">The token for the streamed refresh query.</param>
    private async Task RefreshScopeNativeAsync(
        IQueryable<TEntity> affected,
        NestedSetProviderCapabilities capabilities,
        IGrouping<TScope, EntityEntry<TEntity>> scope,
        CancellationToken cancellationToken
    )
    {
        var entries = new Dictionary<TKey, EntityEntry<TEntity>>(_map.KeyComparer);

        foreach (var entry in scope)
        {
            entries.Add(NestedSetTypedValue<TKey>.Read(entry, _map.KeyProperty), entry);
        }

        var keys = entries.Keys.ToArray();
        var scoped = affected.Where(MatchesValues(_map.ScopeProperty!, [scope.Key]));

        // WHY: The SQL scope filter resolves aliases, so native key equality alone correlates this group's rows.
        var query = keys.Length == 1
            ? scoped.Where(Matches(keys))
            : capabilities.MatchTrackedKeyCollection(scoped, _map.KeyProperty, keys);

        await foreach (var row in query
                           .Select(NativeProjection())
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (entries.TryGetValue((TKey)row[0], out var entry))
            {
                RefreshEntry(entry, row);
            }
        }
    }

    /// <summary>Refreshes non-native tracked keys for a hierarchy whose TreeId is its complete partition key.</summary>
    private async Task RefreshScopelessAsync(
        IQueryable<TEntity> source,
        CancellationToken cancellationToken
    )
    {
        foreach (var filter in AffectedIntervals())
        {
            var affected = source.Where(filter);
            var rowFilter = RowFilter(filter);

            foreach (var batch in _tracked.Chunk(NestedSetBatch.MaximumRows))
            {
                var keys = batch
                    .Select(entry => NestedSetTypedValue<TKey>.Read(entry, _map.KeyProperty))
                    .ToArray();

                var rows = await NestedSetTrackedRowset<TEntity>
                    .Match(affected, _map.Key, keys)
                    .Where(rowFilter)
                    .Select(_refreshProjection)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var row in rows)
                {
                    RefreshEntry(batch[(int)row[0]], row);
                }
            }
        }
    }

    /// <summary>
    ///     Streams tracked native identities through one key parameter per authoritative interval query.
    /// </summary>
    private async Task RefreshNativeAsync(
        NestedSetProviderCapabilities capabilities,
        CancellationToken cancellationToken
    )
    {
        // WHY: NodeKey can repeat in another Scope unless it is an EF key by itself, and only a repeatable key
        // correlates both persisted identity parts. Neither form issues one query per scope or trusts stale bounds.
        var correlateScope = _map.Scope is not null && !_map.HasUniqueNodeKey;
        var byKey = new Dictionary<TKey, EntityEntry<TEntity>>(_map.KeyComparer);
        var byScope = new Dictionary<TScope, Dictionary<TKey, EntityEntry<TEntity>>>(_scopes.Comparer);
        var keys = new HashSet<TKey>(_map.KeyComparer);

        foreach (var entry in _tracked)
        {
            var values = entry.CurrentValues;
            var key = NestedSetTypedValue<TKey>.Read(values, _map.KeyProperty);
            keys.Add(key);

            if (!correlateScope)
            {
                byKey.Add(key, entry);

                continue;
            }

            var scope = NestedSetTypedValue<TScope>.Read(values, _map.ScopeProperty!);

            if (!byScope.TryGetValue(scope, out var scopedEntries))
            {
                scopedEntries = new Dictionary<TKey, EntityEntry<TEntity>>(_map.KeyComparer);
                byScope.Add(scope, scopedEntries);
            }

            scopedEntries.Add(key, entry);
        }

        var projection = NativeProjection();
        var keyValues = keys.ToArray();
        var source = Nodes();

        foreach (var filter in AffectedIntervals())
        {
            var affected = source.Where(filter);

            // WHY: EF's default collection expansion can exceed SQL Server's scalar parameter limit.
            // The explicit override uses the verified provider transport; singleton lookups retain simple equality.
            var query = keyValues.Length == 1
                ? affected.Where(Matches(keyValues))
                : capabilities.MatchTrackedKeyCollection(affected, _map.KeyProperty, keyValues);

            await foreach (var row in query
                               .Select(projection)
                               .AsAsyncEnumerable()
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                // WHY: Streaming avoids retaining another complete projected row array for a large tracker.
                // The enclosing coordinated save still owns the unchanged pre-save rollback snapshot.
                var entries = correlateScope ? byScope.GetValueOrDefault((TScope)row[^1]) : byKey;

                if (entries is not null
                    && entries.TryGetValue((TKey)row[0], out var entry))
                {
                    RefreshEntry(entry, row);
                }
            }
        }
    }

    /// <summary>Projects the native key, refreshed values, and persisted scope without payload columns.</summary>
    private Expression<Func<TEntity, object[]>> NativeProjection()
    {
        if (_nativeProjection is not null)
        {
            return _nativeProjection;
        }

        var parameter = Expression.Parameter(typeof(TEntity), "node");
        var values = new[] { _map.KeyProperty }
            .Concat(_refreshProperties)
            .Concat(_map.ScopeProperty is { } scopeProperty ? [scopeProperty] : [])
            .Select(property => Expression.Convert(NestedSetExpressions.Property(parameter, property), typeof(object)));

        return _nativeProjection = Expression.Lambda<Func<TEntity, object[]>>(
            Expression.NewArrayInit(typeof(object), values),
            parameter);
    }

    /// <summary>Rebinds an authoritative interval predicate to the entity carried by an ordinal row.</summary>
    private static Expression<Func<NestedSetTrackedRowset<TEntity>.Row, bool>> RowFilter(
        Expression<Func<TEntity, bool>> filter
    )
    {
        var parameter = Expression.Parameter(typeof(NestedSetTrackedRowset<TEntity>.Row), "row");
        var entity = Expression.Property(parameter, nameof(NestedSetTrackedRowset<>.Row.Entity));
        var body = new ParameterReplacer(filter.Parameters[0], entity).Visit(filter.Body);

        return Expression.Lambda<Func<NestedSetTrackedRowset<TEntity>.Row, bool>>(body, parameter);
    }

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
        foreach (var tree in _changedIntervals)
        {
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

    /// <summary>Rebinds entity parameters when composing scope, coordinate, and ordinal row predicates.</summary>
    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _source;
        private readonly Expression _target;

        /// <summary>Retains only expression nodes for one bounded composition.</summary>
        internal ParameterReplacer(
            ParameterExpression source,
            Expression target
        )
        {
            _source = source;
            _target = target;
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        ) => node == _source ? _target : node;
    }

    /// <summary>Synchronizes only library-managed coordinates and store-generated concurrency values.</summary>
    /// <param name="entry">The tracked entry, whose payload originals and flags must remain intact.</param>
    /// <param name="row">The input ordinal followed by the selected structural and concurrency values.</param>
    private void RefreshEntry(
        EntityEntry<TEntity> entry,
        object[] row
    )
    {
        var pendingPayload = entry.State == EntityState.Modified;
        IUpdateEntry? generated = null;

        for (var index = 0; index < _refreshProperties.Length; index++)
        {
            var metadata = _refreshProperties[index];
            var property = NestedSetTrackedProperty.Property(entry, metadata);
            if (pendingPayload
                && metadata.IsConcurrencyToken
                && (metadata.ValueGenerated & ValueGenerated.OnUpdate) != 0)
            {
                // WHY: EF's normal generated-value propagation preserves the pending original in its sidecar.
                // Passing false instead would replace that original; clearing IsModified would lose the new value.
                // WHY: The public update adapter exposes this context's existing entry without relying on EF's
                // internal EntityEntry implementation or changing the caller's tracking strategy.
                var primaryKey = _map.EntityType.FindPrimaryKey()
                    ?? throw new InvalidOperationException("The ordered hierarchy entity requires a primary key.");

                generated ??= _updates.TryGetEntry(
                        primaryKey,
                        primaryKey
                            .Properties
                            .Select(property => entry.Property(property.Name).CurrentValue)
                            .ToArray())
                    ?? throw new InvalidOperationException("The ordered entry is no longer tracked.");

                generated.SetStoreGeneratedValue(metadata, row[index + 1], setModified: true);
            }
            else
            {
                // WHY: Bulk SQL bypasses EF; accept managed structure and tokens on previously unchanged siblings
                // without marking those siblings as caller-authored payload updates.
                property.CurrentValue = row[index + 1];

                if (NestedSetTrackedProperty.HasOriginalValue(metadata))
                {
                    property.OriginalValue = row[index + 1];
                }

                property.IsModified = false;
            }
        }
    }

    /// <summary>Returns the exact ordinary or named shared hierarchy set with application filters bypassed.</summary>
    private IQueryable<TEntity> Nodes() => NestedSetEntityAccess<TEntity>
        .Set(_context, _map.EntityType)
        .IgnoreQueryFilters()
        .AsNoTracking();

    /// <summary>Builds balanced parameterized key predicates without CLR key or collation assumptions.</summary>
    /// <param name="keys">The identities requested by the tracker or canonical scope lookup.</param>
    /// <returns>A provider-translatable equality disjunction using mapped key values.</returns>
    private Expression<Func<TEntity, bool>> Matches(
        TKey[] keys
    ) => MatchesValues(_map.KeyProperty, keys);

    /// <summary>Builds a balanced equality disjunction using the property's native database comparison.</summary>
    /// <typeparam name="TValue">The mapped key or scope CLR type.</typeparam>
    /// <param name="property">The finalized key or scope mapping, including its converter.</param>
    /// <param name="values">The parameter values, without applying CLR comparison or sorting.</param>
    /// <returns>A predicate that preserves binary equality and configured database collations.</returns>
    private static Expression<Func<TEntity, bool>> MatchesValues<TValue>(
        IProperty property,
        TValue[] values
    ) => NestedSetKeyFilter<TEntity>.Matches(property, values);
}
