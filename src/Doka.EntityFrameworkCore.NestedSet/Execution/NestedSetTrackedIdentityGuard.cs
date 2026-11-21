namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Protects tracked hierarchy entries using persisted membership and exact provider snapshots.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
/// <typeparam name="TTreeId">The exact mapped TreeId type.</typeparam>
/// <typeparam name="TScope">The exact Scope type, or the internal scopeless marker.</typeparam>
internal sealed partial class NestedSetTrackedIdentityGuard<TEntity, TKey, TTreeId, TScope> : IDisposable
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

    private readonly IReadOnlyList<Candidate> _candidates;

    private readonly DbContext _context;
    private readonly NestedSetProviderComparer<TKey> _keyComparer;
    private readonly NestedSetMapping<TEntity, TKey, TScope> _map;
    private readonly IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> _requests;
    private readonly NestedSetProviderComparer<TTreeId> _treeComparer;
    private readonly bool _usesNativeScopeComparison;
    private bool _newHierarchyEntry;
    private bool _observing;

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
            && (scope.GetTypeMapping()
                    .Converter?.ProviderClrType
                ?? scope.ClrType)
            == typeof(string)
            && NestedSetProviderCapabilities.Resolve(context)
                .UsesNativeScopeComparison;

        _context.ChangeTracker.Tracked += OnTracked;
        _observing = true;
    }

    /// <summary>Gets whether captured entries require native equality probes and a late state check.</summary>
    internal bool HasCandidates => _candidates.Count != 0;

    /// <summary>Removes the temporary attachment observer before structural work or on any failed boundary.</summary>
    public void Dispose()
    {
        if (_observing)
        {
            _context.ChangeTracker.Tracked -= OnTracked;
            _observing = false;
        }
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
            candidates is null
                ? Array.Empty<Candidate>()
                : candidates,
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

                // WHY: Validated lock-request order stays stable while membership and registry ownership are checked.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var requestIndex = 0; requestIndex < requests.Count; requestIndex++)
                {
                    var request = requests[requestIndex];

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

    /// <summary>Rejects attachments, state changes or structural identity edits during awaited native reads.</summary>
    internal void VerifyUnchanged()
    {
        if (_newHierarchyEntry)
        {
            throw ChangedTracker();
        }

        // WHY: Repeated late checks need no iterator: the owned snapshot stays fixed through the awaited boundary.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int index = 0, count = _candidates.Count; index < count; index++)
        {
            var (entry, entityState, propertyValues, tKey, tScope, tTreeId) = _candidates[index];

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

        /// <inheritdoc />
        public IEnumerator<Candidate> GetEnumerator()
        {
            // WHY: Expose the privately owned blocks in their existing ordinal order without copying their contents.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
