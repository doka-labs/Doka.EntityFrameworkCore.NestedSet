namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

/// <summary>Implements bounded, scalar-only refresh queries for exact finalized model types.</summary>
internal sealed class NestedSetTrackedStructure<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly DbContext _context;
    private readonly NestedSetMapping<TEntity, TKey, TScope> _map;
    private readonly EntityEntry<TEntity>[] _entries;
    private readonly IProperty[] _properties;
    private readonly Expression<Func<NestedSetTrackedRowset<TEntity>.Row, object[]>> _projection;
    private readonly Expression<Func<TEntity, object[]>> _nativeProjection;

    /// <summary>Creates a complete refresh capture after persisted membership has been resolved.</summary>
    private NestedSetTrackedStructure(
        DbContext context,
        NestedSetMapping<TEntity, TKey, TScope> map,
        EntityEntry<TEntity>[] entries,
        IProperty[] properties,
        Expression<Func<NestedSetTrackedRowset<TEntity>.Row, object[]>> projection,
        Expression<Func<TEntity, object[]>> nativeProjection
    )
    {
        _context = context;
        _map = map;
        _entries = entries;
        _properties = properties;
        _projection = projection;
        _nativeProjection = nativeProjection;
    }

    /// <summary>Captures tracked rows belonging to locked trees, or returns null when no rows need refresh.</summary>
    internal static async Task<NestedSetTrackedStructure<TEntity, TKey, TTreeId, TScope>?> CaptureAsync(
        DbContext context,
        NestedSetMapping<TEntity, TKey, TScope> map,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests,
        bool includeModified,
        CancellationToken cancellationToken
    )
    {
        var tracked = NestedSetEntityAccess<TEntity>
            .Entries(context, map.EntityType)
            .Where(entry => entry.State == EntityState.Unchanged
                || (includeModified && entry.State == EntityState.Modified))
            .ToArray();

        if (tracked.Length == 0)
        {
            return null;
        }

        var source = NestedSetEntityAccess<TEntity>
            .Set(context, map.EntityType)
            .IgnoreQueryFilters()
            .AsNoTracking();

        var lockedTrees = requests
            .Chunk(NestedSetBatch.MaximumRows)
            .Select(requestBatch => (Nodes: LockedNodes(map, requestBatch), Rows: LockedRows(map, requestBatch)))
            .ToArray();

        var affected = new List<EntityEntry<TEntity>>();
        var capabilities = NestedSetProviderCapabilities.Resolve(context);
        var nativeKeys = map.HasNativeKeyEquality && capabilities.SupportsTrackedKeyCollection(map.KeyProperty);

        // WHY: A node key is unique within its scope, not across the whole tracker. Each ordinal batch
        // must therefore use the same scope predicate as the tracked entries it identifies.
        foreach (var scope in GroupByScope(map, tracked))
        {
            var scopedSource = FilterScope(map, source, scope.Key);

            foreach (var batch in scope.Chunk(NestedSetBatch.MaximumRows))
            {
                var keys = batch
                    .Select(entry => NestedSetTypedValue<TKey>.Read(entry, map.KeyProperty))
                    .ToArray();

                var found = new bool[batch.Length];

                foreach (var trees in lockedTrees)
                {
                    if (nativeKeys && keys.Length > 1)
                    {
                        // WHY: Native collection transport has no ordinal. Its verified key equality lets us
                        // correlate the returned keys inside this one scope without a 64-branch UNION rowset.
                        var matched = await capabilities
                            .MatchTrackedKeyCollection(scopedSource.Where(trees.Nodes), map.KeyProperty, keys)
                            .Select(node => EF.Property<TKey>(node, map.Key))
                            .ToArrayAsync(cancellationToken)
                            .ConfigureAwait(false);

                        var identities = new HashSet<TKey>(matched, map.KeyComparer);

                        for (var index = 0; index < keys.Length; index++)
                        {
                            found[index] |= identities.Contains(keys[index]);
                        }

                        continue;
                    }

                    // WHY: The locked-tree predicate is applied once outside UNION ALL. Repeating it in every key
                    // branch would grow each statement with the product of tracked keys and locked trees.
                    var ordinals = await NestedSetTrackedRowset<TEntity>
                        .Match(scopedSource, map.Key, keys)
                        .Where(trees.Rows)
                        .Select(row => row.Ordinal)
                        .Distinct()
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var matchedOrdinal in ordinals)
                    {
                        found[matchedOrdinal] = true;
                    }
                }

                for (var index = 0; index < batch.Length; index++)
                {
                    if (found[index])
                    {
                        affected.Add(batch[index]);
                    }
                }
            }
        }

        var entries = affected.ToArray();

        if (entries.Length == 0)
        {
            return null;
        }

        // WHY: Native membership is known before constructing the capture. Empty results avoid projection
        // allocations, and affected captures cannot expose partially initialized refresh state.
        var structure = new[]
            {
                map.TreeIdProperty,
                map.ParentProperty,
                map.LeftProperty,
                map.RightProperty,
                map.DepthProperty,
                map.PositionProperty,
            }
            .Concat(map.ScopeProperty is { } scopeProperty ? [scopeProperty] : []);

        var properties = NestedSetRefreshProperties.Collect(map.EntityType, structure);

        var parameter = Expression.Parameter(typeof(NestedSetTrackedRowset<TEntity>.Row), "row");
        var entity = Expression.Property(parameter, nameof(NestedSetTrackedRowset<>.Row.Entity));
        var ordinal = Expression.Property(parameter, nameof(NestedSetTrackedRowset<>.Row.Ordinal));
        var values = new[] { Expression.Convert(ordinal, typeof(object)) }.Concat(
            properties.Select(property => NestedSetRefreshProperties.Project(entity, property)));

        var projection = Expression.Lambda<Func<NestedSetTrackedRowset<TEntity>.Row, object[]>>(
            Expression.NewArrayInit(typeof(object), values),
            parameter);

        var nativeParameter = Expression.Parameter(typeof(TEntity), "node");
        var nativeValues = new[] { map.KeyProperty }
            .Concat(properties)
            .Select(property => NestedSetRefreshProperties.Project(nativeParameter, property));

        var nativeProjection = Expression.Lambda<Func<TEntity, object[]>>(
            Expression.NewArrayInit(typeof(object), nativeValues),
            nativeParameter);

        return new NestedSetTrackedStructure<TEntity, TKey, TTreeId, TScope>(
            context,
            map,
            entries,
            properties,
            projection,
            nativeProjection);
    }

    /// <summary>Synchronizes managed structural values and detaches rows removed by the mutation.</summary>
    internal async Task RefreshAsync(
        CancellationToken cancellationToken
    )
    {
        var source = NestedSetEntityAccess<TEntity>
            .Set(_context, _map.EntityType)
            .IgnoreQueryFilters()
            .AsNoTracking();

        var capabilities = NestedSetProviderCapabilities.Resolve(_context);
        var nativeKeys = _map.HasNativeKeyEquality && capabilities.SupportsTrackedKeyCollection(_map.KeyProperty);

        foreach (var scope in GroupByScope(_map, _entries))
        {
            var scopedSource = FilterScope(_map, source, scope.Key);

            foreach (var batch in scope.Chunk(NestedSetBatch.MaximumRows))
            {
                var keys = batch
                    .Select(entry => NestedSetTypedValue<TKey>.Read(entry, _map.KeyProperty))
                    .ToArray();

                if (nativeKeys && keys.Length > 1)
                {
                    // WHY: Native identity projection avoids ordinal UNION branches, and scope grouping
                    // keeps a tenant-local key from refreshing another tenant's tracked entity.
                    var nativeRows = await capabilities
                        .MatchTrackedKeyCollection(scopedSource, _map.KeyProperty, keys)
                        .Select(_nativeProjection)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var byKey = batch.ToDictionary(
                        entry => NestedSetTypedValue<TKey>.Read(entry, _map.KeyProperty),
                        _map.KeyComparer);

                    var persistedKeys = new HashSet<TKey>(_map.KeyComparer);

                    foreach (var row in nativeRows)
                    {
                        var key = (TKey)row[0];
                        persistedKeys.Add(key);
                        RefreshEntry(byKey[key], row);
                    }

                    foreach (var entry in batch)
                    {
                        if (!persistedKeys.Contains(NestedSetTypedValue<TKey>.Read(entry, _map.KeyProperty)))
                        {
                            entry.State = EntityState.Detached;
                        }
                    }

                    continue;
                }

                var rows = await NestedSetTrackedRowset<TEntity>
                    .Match(scopedSource, _map.Key, keys)
                    .Select(_projection)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                var persisted = new bool[batch.Length];

                foreach (var row in rows)
                {
                    var ordinal = (int)row[0];
                    persisted[ordinal] = true;
                    RefreshEntry(batch[ordinal], row);
                }

                for (var index = 0; index < batch.Length; index++)
                {
                    if (!persisted[index])
                    {
                        // WHY: A set-based delete bypasses tracking. Detaching prevents a later SaveChanges call from
                        // treating a row known to be gone as a still-current aggregate member.
                        batch[index].State = EntityState.Detached;
                    }
                }
            }
        }
    }

    /// <summary>Separates tenant-local node keys before any rowset correlation.</summary>
    private static IEnumerable<IGrouping<TScope, EntityEntry<TEntity>>> GroupByScope(
        NestedSetMapping<TEntity, TKey, TScope> map,
        IEnumerable<EntityEntry<TEntity>> entries
    ) => entries.GroupBy(
        entry => map.Scope is null ? default! : NestedSetTypedValue<TScope>.Read(entry, map.ScopeProperty!),
        map.ScopeComparer);

    /// <summary>Keeps a rowset's key correlation within the tracked node's immutable scope.</summary>
    private static IQueryable<TEntity> FilterScope(
        NestedSetMapping<TEntity, TKey, TScope> map,
        IQueryable<TEntity> source,
        TScope scope
    ) => map.Scope is null ? source : source.Where(NestedSetKeyFilter<TEntity>.Matches(map.ScopeProperty!, [scope]));

    /// <summary>Matches nodes in one bounded batch of exact locked tree identities.</summary>
    private static Expression<Func<TEntity, bool>> LockedNodes(
        NestedSetMapping<TEntity, TKey, TScope> map,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests
    )
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");

        return Expression.Lambda<Func<TEntity, bool>>(LockedTree(map, parameter, requests), parameter);
    }

    /// <summary>Matches ordinal rows whose entity lies in one bounded batch of exact locked tree identities.</summary>
    private static Expression<Func<NestedSetTrackedRowset<TEntity>.Row, bool>> LockedRows(
        NestedSetMapping<TEntity, TKey, TScope> map,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests
    )
    {
        var parameter = Expression.Parameter(typeof(NestedSetTrackedRowset<TEntity>.Row), "row");
        var entity = Expression.Property(parameter, nameof(NestedSetTrackedRowset<TEntity>.Row.Entity));

        return Expression.Lambda<Func<NestedSetTrackedRowset<TEntity>.Row, bool>>(
            LockedTree(map, entity, requests),
            parameter);
    }

    /// <summary>Builds a balanced disjunction of Scope and TreeId equality using database comparisons.</summary>
    private static Expression LockedTree(
        NestedSetMapping<TEntity, TKey, TScope> map,
        Expression entity,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests
    )
    {
        return Combine(0, requests.Count);

        Expression Combine(
            int start,
            int count
        )
        {
            if (count == 1)
            {
                var request = requests[start];
                var tree = NestedSetKeyFilter<TEntity>.Matches(entity, map.TreeIdProperty, [request.TreeId]);

                return map.ScopeProperty is { } scope
                    ? Expression.AndAlso(NestedSetKeyFilter<TEntity>.Matches(entity, scope, [request.Scope]), tree)
                    : tree;
            }

            var half = count / 2;

            return Expression.OrElse(Combine(start, half), Combine(start + half, count - half));
        }
    }

    /// <summary>Refreshes managed values while preserving a tracked entry's pending payload semantics.</summary>
    private void RefreshEntry(
        EntityEntry<TEntity> entry,
        object[] row
    ) => NestedSetTrackedRefresh.Apply(entry, _properties, row);
}
