namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetTrackedIdentityGuard<TEntity, TKey, TTreeId, TScope> : IDisposable
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Rejects entries whose actual persisted rows belong to any locked mutation tree.</summary>
    /// <param name="cancellationToken">The token used by the bounded native database probes.</param>
    /// <returns>A task that completes only when every uncertain tracked row is unaffected.</returns>
    /// <remarks>The executor calls this method after acquiring all requested registry locks.</remarks>
    internal async Task RequireUnaffectedAsync(
        CancellationToken cancellationToken
    )
    {
        var source = NestedSetEntityAccess<TEntity>
            .Set(_context, _map.EntityType)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .TagWith(QueryTag);

        var capabilities = NestedSetProviderCapabilities.Resolve(_context);
        var nativeGroups = _map.HasNativeKeyEquality && capabilities.SupportsTrackedKeyCollection(_map.KeyProperty)
            ? CreateNativeGroups()
            : null;

        // WHY: The same owned candidates apply to every request batch. Build bounded native predicates once;
        // grouping inside each pack prevents one query per Scope when scopes contain only a few tracked rows.
        var scalarPredicates = nativeGroups is null
            ? CreateScalarPredicates()
            : null;

        // WHY: Batch boundaries must match the native rowset request budget without copying the whole request list.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var requestStart = 0; requestStart < _requests.Count; requestStart += NestedSetBatch.MaximumRows)
        {
            var requestCount = Math.Min(NestedSetBatch.MaximumRows, _requests.Count - requestStart);
            var affected = source.Where(RequestPredicate(requestStart, requestCount));

            if (nativeGroups is not null)
            {
                foreach (var group in nativeGroups)
                {
                    var scoped = _map.Scope is null
                        ? affected
                        : affected.Where(ScopePredicate(group.Scope));

                    // WHY: Verified native keys can share one collection parameter regardless of tracker width.
                    // A mapped Scope predicate preserves tenant-local keys and SQL-equal Scope aliases; Doka's
                    // existing equality join avoids scanning the JSON collection for every persisted row.
                    if (await capabilities
                        .MatchTrackedKeyCollection(scoped, _map.KeyProperty, group.Keys)
                        .AnyAsync(cancellationToken)
                        .ConfigureAwait(false))
                    {
                        throw AffectedTrackedEntry();
                    }
                }

                continue;
            }

            foreach (var predicate in scalarPredicates!)
            {
                // WHY: Persisted Scope + NodeKey membership also catches moved rows with a stale TreeId.
                // SQLite counts an EXISTS subquery's expression height in the outer and inner resolution scopes;
                // a direct SELECT 1 with a one-row limit keeps the indexed predicate and avoids duplicated depth.
                if (await affected
                        .Where(predicate)
                        .Select(static _ => 1)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false)
                    != 0)
                {
                    throw AffectedTrackedEntry();
                }
            }
        }
    }

    /// <summary>Builds each provider-representation Scope group and its unique native key array once.</summary>
    private NativeKeyGroup[] CreateNativeGroups()
    {
        if (_map.Scope is null)
        {
            // WHY: The scopeless marker has no database value. A single set avoids unnecessary dictionary keys
            // while still deduplicating the collection used by Doka's equality join.
            var keys = new HashSet<TKey>(_candidates.Count, _map.KeyComparer);

            // WHY: The owned block buffer allocates an iterator per traversal; indexing avoids that grouping cost.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (int index = 0, count = _candidates.Count; index < count; index++)
            {
                keys.Add(_candidates[index].Key);
            }

            return [new NativeKeyGroup(default!, keys.ToArray())];
        }

        var scopes = new Dictionary<TScope, HashSet<TKey>>(_map.ScopeComparer);

        // WHY: The captured block buffer is stable; indexing avoids its iterator while retaining every scoped identity.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int index = 0, count = _candidates.Count; index < count; index++)
        {
            var candidate = _candidates[index];

            if (!scopes.TryGetValue(candidate.Scope, out var keys))
            {
                keys = new HashSet<TKey>(_map.KeyComparer);
                scopes.Add(candidate.Scope, keys);
            }

            keys.Add(candidate.Key);
        }

        // WHY: Provider representations preserve values that broader domain comparers merge. SQL-equal aliases
        // may form extra groups, but each group has its native Scope filter and cannot omit a persisted match.
        // Retain every original Candidate for the late tracker check; deduplication applies only to SQL input.
        return scopes
            .Select(group => new NativeKeyGroup(group.Key, group.Value.ToArray()))
            .ToArray();
    }

    /// <summary>Builds a bounded native disjunction of complete locked tree identities.</summary>
    private Expression<Func<TEntity, bool>> RequestPredicate(
        int start,
        int count
    )
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");

        return Expression.Lambda<Func<TEntity, bool>>(
            Combine(
                start,
                count,
                index =>
                {
                    var request = _requests[index];
                    var tree = NestedSetKeyFilter<TEntity>.Matches(parameter, _map.TreeIdProperty, [request.TreeId]);

                    // WHY: The request retains both known role types. Mapped scalar equality preserves converters and
                    // native collations without erasing the structural predicate's parameter values.
                    return _map.Scope is null
                        ? tree
                        : Expression.AndAlso(
                            tree,
                            NestedSetKeyFilter<TEntity>.Matches(parameter, _map.ScopeProperty!, [request.Scope]));
                }),
            parameter);
    }

    /// <summary>Builds reusable scalar predicates from bounded packs of complete Scope and NodeKey pairs.</summary>
    private Expression<Func<TEntity, bool>>[] CreateScalarPredicates()
    {
        var predicates = new List<Expression<Func<TEntity, bool>>>();

        // WHY: Candidate chunks preserve the scalar parameter budget while retaining each complete key/scope pair.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var start = 0; start < _candidates.Count; start += MaximumScalarCandidates)
        {
            var count = Math.Min(MaximumScalarCandidates, _candidates.Count - start);
            var parameter = Expression.Parameter(typeof(TEntity), "node");

            if (_map.Scope is null)
            {
                var keys = new TKey[count];

                // WHY: Copy only the active candidate slice into its matching key-array slots.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < count; index++)
                {
                    keys[index] = _candidates[start + index].Key;
                }

                predicates.Add(
                    Expression.Lambda<Func<TEntity, bool>>(
                        NestedSetKeyFilter<TEntity>.Matches(parameter, _map.KeyProperty, keys),
                        parameter));

                continue;
            }

            var scopes = new Dictionary<TScope, List<TKey>>(_map.ScopeComparer);

            // WHY: Traverse only the bounded candidate slice without allocating a slice iterator.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < count; index++)
            {
                var candidate = _candidates[start + index];

                if (!scopes.TryGetValue(candidate.Scope, out var keys))
                {
                    keys = new List<TKey>();
                    scopes.Add(candidate.Scope, keys);
                }

                // WHY: Converted keys can have broader model equality than stored equality. Preserve every
                // candidate instead of deduplicating through a model comparer and dropping a persisted match.
                keys.Add(candidate.Key);
            }

            var groups = scopes.ToArray();
            var predicate = Combine(
                0,
                groups.Length,
                index => Expression.AndAlso(
                    ScopeMatch(parameter, groups[index].Key),
                    NestedSetKeyFilter<TEntity>.Matches(parameter, _map.KeyProperty, groups[index].Value)));

            predicates.Add(Expression.Lambda<Func<TEntity, bool>>(predicate, parameter));
        }

        return predicates.ToArray();
    }

    /// <summary>Preserves one group's native Scope identity without changing the indexed request predicate.</summary>
    private Expression<Func<TEntity, bool>> ScopePredicate(
        TScope value
    )
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");

        return Expression.Lambda<Func<TEntity, bool>>(ScopeMatch(parameter, value), parameter);
    }

    /// <summary>Builds native mapped Scope equality for both collection groups and bounded scalar candidates.</summary>
    private Expression ScopeMatch(
        Expression entity,
        TScope value
    )
    {
        var access = NestedSetExpressions.Property(entity, _map.Scope!, typeof(TScope));
        Expression<Func<TScope>> captured = () => value;

        // WHY: Repeated MySQL string equalities can fold case-distinct parameters under a connection collation.
        // Compare the candidate through the column's native function, retaining unknown inherited defaults too.
        return _usesNativeScopeComparison
            ? NestedSetNativeScopeEquality<TScope>.Match(access, captured.Body)
            : NestedSetTypedEquality<TScope>.Equal(access, captured.Body);
    }

    /// <summary>Keeps bounded disjunction depth logarithmic for every supported relational provider.</summary>
    private static Expression Combine(
        int start,
        int count,
        Func<int, Expression> predicate
    )
    {
        if (count == 1)
        {
            return predicate(start);
        }

        var half = count / 2;

        return Expression.OrElse(Combine(start, half, predicate), Combine(start + half, count - half, predicate));
    }
}
