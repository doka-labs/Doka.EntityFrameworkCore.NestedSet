namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Protects tracked hierarchy entries using persisted membership and exact provider snapshots.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
/// <typeparam name="TTreeId">The exact mapped TreeId type.</typeparam>
/// <typeparam name="TScope">The exact Scope type, or the internal scopeless marker.</typeparam>
internal sealed class NestedSetTrackedIdentityGuard<TEntity, TKey, TTreeId, TScope> : IDisposable
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Identifies scalar-only guard reads in provider evidence without exposing identity values.</summary>
    internal const string QueryTag = "NestedSet native tracked identity guard";

    /// <summary>Bounds scalar candidates before grouping their complete Scope and NodeKey identities.</summary>
    // WHY: At most twice 768 candidate parameters plus two for each of 64 requested trees stays below
    // SQL Server's 2,098-parameter command budget, including fixed collection expansion padding.
    private const int MaximumScalarCandidates = 768;

    private readonly DbContext _context;
    private readonly NestedSetMapping<TEntity, TKey, TScope> _map;
    private readonly IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> _requests;
    private readonly IReadOnlyList<Candidate> _candidates;
    private readonly NestedSetProviderComparer<TKey> _keyComparer;
    private readonly NestedSetProviderComparer<TTreeId> _treeComparer;
    private readonly bool _usesNativeScopeComparison;
    private bool _observing;
    private bool _newHierarchyEntry;

    /// <summary>Observes only hierarchy attachments during the pending native equality checks.</summary>
    private NestedSetTrackedIdentityGuard(
        DbContext context,
        NestedSetMapping<TEntity, TKey, TScope> map,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests,
        IReadOnlyList<Candidate> candidates,
        NestedSetProviderComparer<TKey> keyComparer,
        NestedSetProviderComparer<TTreeId> treeComparer
    )
    {
        _context = context;
        _map = map;
        _requests = requests;
        _candidates = candidates;
        _keyComparer = keyComparer;
        _treeComparer = treeComparer;
        _usesNativeScopeComparison = map.ScopeProperty is { } scope
            && (scope.GetTypeMapping().Converter?.ProviderClrType ?? scope.ClrType) == typeof(string)
            && NestedSetProviderCapabilities.Resolve(context).Kind == NestedSetProviderKind.MySql;

        _context.ChangeTracker.Tracked += OnTracked;
        _observing = true;
    }

    /// <summary>Rejects known affected identities immediately and snapshots only uncertain hierarchy entries.</summary>
    /// <param name="context">The caller-owned context after its initial mutation-state check.</param>
    /// <param name="map">The exact typed mapping owned by the mutation executor.</param>
    /// <param name="requests">The already validated complete tree lock identities.</param>
    /// <returns>A temporary guard, or null only when a coordinated save already owns tracking.</returns>
    internal static NestedSetTrackedIdentityGuard<TEntity, TKey, TTreeId, TScope>? Capture(
        DbContext context,
        NestedSetMapping<TEntity, TKey, TScope> map,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests
    )
    {
        if (NestedSetSaveChanges.IsManagedMutation(context))
        {
            // WHY: Coordinated saves own their tracked snapshots, lock plan and subsequent structural refresh.
            return null;
        }

        var tracker = context.ChangeTracker;
        var automaticDetection = tracker.AutoDetectChangesEnabled;
        CandidateBuffer? candidates = null;
        var keyComparer = NestedSetTypedValue<TKey>.Comparer(map.KeyProperty);
        var treeComparer = NestedSetTypedValue<TTreeId>.Comparer(map.TreeIdProperty);
        HashSet<TTreeId>? requestedTrees = null;
        Dictionary<TScope, HashSet<TTreeId>>? requestedScopes = null;

        try
        {
            // WHY: The executor already detected pending CLR edits. Enumerating this hierarchy must not run a
            // second global scan or create retained entry wrappers for unrelated clean application entities.
            tracker.AutoDetectChangesEnabled = false;

            foreach (var entry in NestedSetEntityAccess<TEntity>.Entries(context, map.EntityType))
            {
                if (entry.State == EntityState.Detached)
                {
                    continue;
                }

                var values = entry.CurrentValues;
                var treeId = NestedSetTypedValue<TTreeId>.Read(values, map.TreeIdProperty);
                var scope = map.ScopeProperty is { } scopeProperty
                    ? NestedSetTypedValue<TScope>.Read(values, scopeProperty)
                    : default!;

                if (treeId is null
                    || (map.ScopeProperty is not null && scope is null))
                {
                    // WHY: EF permits unchanged attached reference identities to retain null outside its keys.
                    // Reject missing required roles before invoking converters or snapshotting their values.
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidContext,
                        "Tracked hierarchy entries must have non-null TreeId and configured Scope values.");
                }

                if (IsRequested(treeId, scope))
                {
                    throw AffectedTrackedEntry();
                }

                candidates ??= new CandidateBuffer();

                // WHY: One public CurrentValues wrapper remains live for late typed reads, including shadow and
                // temporary sidecar storage. Independent snapshots survive shallow domain comparer mutations.
                candidates.Add(
                    new Candidate(
                        entry,
                        entry.State,
                        values,
                        NestedSetTypedValue<TKey>.Snapshot(
                            map.KeyProperty,
                            NestedSetTypedValue<TKey>.Read(values, map.KeyProperty)),
                        map.ScopeProperty is null
                            ? default!
                            : NestedSetTypedValue<TScope>.Snapshot(map.ScopeProperty, scope),
                        NestedSetTypedValue<TTreeId>.Snapshot(map.TreeIdProperty, treeId)));
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = automaticDetection;
        }

        // WHY: An initially empty hierarchy tracker can still receive unchanged attachments from a registry-lock
        // interceptor. Observe those attachments before any await, while retaining no empty per-entry list.
        return new NestedSetTrackedIdentityGuard<TEntity, TKey, TTreeId, TScope>(
            context,
            map,
            requests,
            candidates is null ? Array.Empty<Candidate>() : candidates,
            keyComparer,
            treeComparer);

        bool IsRequested(
            TTreeId treeId,
            TScope scope
        )
        {
            if (map.ScopeProperty is null)
            {
                requestedTrees ??= new HashSet<TTreeId>(requests.Select(request => request.TreeId), treeComparer);

                return requestedTrees.Contains(treeId);
            }

            if (requestedScopes is null)
            {
                // WHY: Complete provider-equal pairs are definite matches. Building membership lazily avoids
                // any index for an empty hierarchy tracker; SQL collation aliases still need native probes.
                requestedScopes = new Dictionary<TScope, HashSet<TTreeId>>(requests.Count, map.ScopeComparer);

                foreach (var request in requests)
                {
                    if (!requestedScopes.TryGetValue(request.Scope, out var trees))
                    {
                        trees = new HashSet<TTreeId>(treeComparer);
                        requestedScopes.Add(request.Scope, trees);
                    }

                    trees.Add(request.TreeId);
                }
            }

            return requestedScopes.TryGetValue(scope, out var requested) && requested.Contains(treeId);
        }
    }

    /// <summary>Gets whether captured entries require native equality probes and a late state check.</summary>
    internal bool HasCandidates => _candidates.Count != 0;

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
        var scalarPredicates = nativeGroups is null ? CreateScalarPredicates() : null;

        for (var requestStart = 0; requestStart < _requests.Count; requestStart += NestedSetBatch.MaximumRows)
        {
            var requestCount = Math.Min(NestedSetBatch.MaximumRows, _requests.Count - requestStart);
            var affected = source.Where(RequestPredicate(requestStart, requestCount));

            if (nativeGroups is not null)
            {
                foreach (var group in nativeGroups)
                {
                    var scoped = _map.Scope is null ? affected : affected.Where(ScopePredicate(group.Scope));

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

            foreach (var candidate in _candidates)
            {
                keys.Add(candidate.Key);
            }

            return [new NativeKeyGroup(default!, keys.ToArray())];
        }

        var scopes = new Dictionary<TScope, HashSet<TKey>>(_map.ScopeComparer);

        foreach (var candidate in _candidates)
        {
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

    /// <summary>Rejects attachments, state changes or structural identity edits during awaited native reads.</summary>
    internal void VerifyUnchanged()
    {
        if (_newHierarchyEntry)
        {
            throw ChangedTracker();
        }

        foreach (var (entry, entityState, propertyValues, tKey, tScope, tTreeId) in _candidates)
        {
            if (entry.State != entityState
                || !_keyComparer.Equals(
                    NestedSetTypedValue<TKey>.Read(propertyValues, _map.KeyProperty),
                    tKey)
                || !_treeComparer.Equals(
                    NestedSetTypedValue<TTreeId>.Read(propertyValues, _map.TreeIdProperty),
                    tTreeId)
                || (_map.ScopeProperty is { } scope
                    && !_map.ScopeComparer.Equals(
                        NestedSetTypedValue<TScope>.Read(propertyValues, scope),
                        tScope)))
            {
                throw ChangedTracker();
            }
        }
    }

    /// <summary>Removes the temporary attachment observer before structural work or on any failed boundary.</summary>
    public void Dispose()
    {
        if (_observing)
        {
            _context.ChangeTracker.Tracked -= OnTracked;
            _observing = false;
        }
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

        for (var start = 0; start < _candidates.Count; start += MaximumScalarCandidates)
        {
            var count = Math.Min(MaximumScalarCandidates, _candidates.Count - start);
            var parameter = Expression.Parameter(typeof(TEntity), "node");

            if (_map.Scope is null)
            {
                var keys = new TKey[count];

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

    /// <summary>Flags new hierarchy attachments without retaining every unrelated application entry.</summary>
    private void OnTracked(
        object? sender,
        EntityTrackedEventArgs args
    )
    {
        if (_map.EntityType.IsAssignableFrom(args.Entry.Metadata))
        {
            _newHierarchyEntry = true;
        }
    }

    /// <summary>Creates the same public error for definite and database-resolved affected membership.</summary>
    private static NestedSetException AffectedTrackedEntry() => new(
        NestedSetErrorCode.InvalidContext,
        "Explicit mutations require affected hierarchy entities to be untracked.");

    /// <summary>Reports callback changes without changing or detaching application-owned tracked instances.</summary>
    private static NestedSetException ChangedTracker() => new(
        NestedSetErrorCode.InvalidContext,
        "Tracked hierarchy entries changed while their native identities were being checked.");

    /// <summary>Keeps one complete native Scope filter with its verified, deduplicated key collection.</summary>
    private readonly record struct NativeKeyGroup(
        TScope Scope,
        TKey[] Keys
    );

    /// <summary>Owns snapshots in small blocks without copying previously captured entry references.</summary>
    private sealed class CandidateBuffer : IReadOnlyList<Candidate>
    {
        private const int BlockSize = 64;
        private readonly List<Candidate[]> _blocks = new();

        /// <inheritdoc />
        public int Count { get; private set; }

        /// <inheritdoc />
        public Candidate this[
            int index
        ]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _blocks[index / BlockSize][index % BlockSize];
            }
        }

        /// <summary>Appends one snapshot and retains earlier live wrappers in their original block.</summary>
        internal void Add(
            Candidate candidate
        )
        {
            if (Count % BlockSize == 0)
            {
                // WHY: A growing List of candidate structs repeatedly copies the entire large value buffer.
                // Fixed blocks copy only the small directory; no pool retains application entry references.
                _blocks.Add(new Candidate[BlockSize]);
            }

            _blocks[^1][Count % BlockSize] = candidate;
            Count++;
        }

        /// <inheritdoc />
        public IEnumerator<Candidate> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Retains one uncertain entry and independent, exact typed structural identity snapshots.</summary>
    private readonly record struct Candidate(
        EntityEntry<TEntity> Entry,
        EntityState State,
        PropertyValues Values,
        TKey Key,
        TScope Scope,
        TTreeId TreeId
    );
}
