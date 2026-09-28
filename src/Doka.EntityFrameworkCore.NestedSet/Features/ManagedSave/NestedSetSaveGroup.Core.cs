namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

/// <summary>Coordinates one hierarchy entity type without materializing its domain payload.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The configured scalar node-key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The immutable scope type or the internal scopeless marker.</typeparam>
internal sealed partial class NestedSetSaveGroup<TEntity, TKey, TTreeId, TScope> : NestedSetSaveGroup
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly DbContext _context;
    private readonly IUpdateAdapter _updates;
    private readonly EntityEntry<TEntity>[] _changed;
    private readonly EntityEntry<TEntity>[] _tracked;
    private readonly NestedSetMapping<TEntity, TKey, TScope> _map;
    private readonly Dictionary<TScope, HashSet<TKey>> _scopes;
    private readonly Dictionary<TScope, HashSet<TTreeId>> _requestedTrees;
    private readonly NestedSetProviderComparer<TTreeId> _treeIdComparer;
    private readonly List<PersistedIdentity> _identities = [];
    private readonly ParentChange[] _parentChanges;
    private ParentChange[]? _orderedParentChanges;
    private readonly List<NestedSetTreeLockRequest<TTreeId, TScope>> _treeRequests = [];
    private readonly IProperty[] _refreshProperties;
    private readonly Expression<Func<NestedSetTrackedRowset<TEntity>.Row, object[]>> _refreshProjection;
    private Expression<Func<TEntity, object[]>>? _nativeProjection;
    private readonly List<TreeIntervals> _changedIntervals = [];

    /// <summary>Captures tracked identities and validated structural metadata before the payload save.</summary>
    /// <param name="context">The context that owns all reads, writes, and tracked entries.</param>
    /// <param name="changed">The entries whose domain ordering values will be saved.</param>
    internal NestedSetSaveGroup(
        DbContext context,
        EntityEntry[] changed
    )
    {
        _context = context;
        _updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        _map = NestedSetMapping<TEntity, TKey, TScope>.For(context, changed[0].Metadata);
        var tracker = context.ChangeTracker;
        var automaticDetection = tracker.AutoDetectChangesEnabled;

        try
        {
            // WHY: Planning already detected changes. The public named set entry API reuses that exact tracked
            // metadata and its sidecars; suppress local detection while creating typed wrappers once per group.
            // CLR-only context.Entry<TEntity>() would lose an ambiguous property-bag entity type name.
            tracker.AutoDetectChangesEnabled = false;
            _changed = changed
                .Select(entry => NestedSetEntityAccess<TEntity>.Entry(context, entry.Metadata, (TEntity)entry.Entity))
                .ToArray();

            _tracked = tracker
                .Entries<TEntity>()
                .Where(entry => entry.Metadata == _map.EntityType)
                .ToArray();
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = automaticDetection;
        }

        // WHY: An application comparer may equate scopes that the database distinguishes. Bookkeeping uses
        // provider representations; SQL later resolves database-equal aliases without dropping another scope.
        _scopes = new Dictionary<TScope, HashSet<TKey>>(_map.ScopeComparer);

        _requestedTrees = new Dictionary<TScope, HashSet<TTreeId>>(_scopes.Comparer);

        _treeIdComparer = new NestedSetProviderComparer<TTreeId>(_map.TreeIdProperty);
        _parentChanges = _changed
            .Where(entry => entry.Property(_map.Parent).IsModified)
            .Select(entry =>
            {
                // WHY: CurrentValues is live tracker metadata, but each access allocates a wrapper. Reuse one
                // for these known roles without cloning values or bypassing temporary and shadow sidecars.
                var values = entry.CurrentValues;
                var parent = NestedSetParent<TKey>.Read(values, _map.ParentProperty);

                if (!parent.HasValue)
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.OperationRejected,
                        "Setting Parent to null requires DetachAsTreeAsync with an explicit new TreeId.");
                }

                return new ParentChange(
                    entry,
                    NestedSetTypedValue<TKey>.Read(values, _map.KeyProperty),
                    entry.Property(_map.Parent).OriginalValue,
                    parent.Value,
                    _map.ScopeProperty is not { } scopeProperty
                        ? default!
                        : NestedSetTypedValue<TScope>.Read(values, scopeProperty));
            })
            .ToArray();

        var structure = new[]
        {
            _map.LeftProperty,
            _map.RightProperty,
            _map.DepthProperty,
            _map.PositionProperty,
            _map.ParentProperty,
        };

        _refreshProperties = structure
            .Concat(
                _map
                    .EntityType
                    .GetFlattenedProperties()
                    .Where(property =>
                        property.IsConcurrencyToken && (property.ValueGenerated & ValueGenerated.OnUpdate) != 0))
            .Distinct()
            .ToArray();

        var parameter = Expression.Parameter(typeof(NestedSetTrackedRowset<TEntity>.Row), "row");
        var entity = Expression.Property(parameter, nameof(NestedSetTrackedRowset<>.Row.Entity));
        var ordinal = Expression.Property(parameter, nameof(NestedSetTrackedRowset<>.Row.Ordinal));
        var projection = new[] { Expression.Convert(ordinal, typeof(object)) }.Concat(
            _refreshProperties.Select(property => Expression.Convert(
                NestedSetExpressions.Property(entity, property),
                typeof(object))));

        _refreshProjection = Expression.Lambda<Func<NestedSetTrackedRowset<TEntity>.Row, object[]>>(
            Expression.NewArrayInit(typeof(object), projection),
            parameter);
    }

    /// <inheritdoc />
    internal override async Task ResolveScopesAsync(
        List<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    )
    {
        foreach (var entry in _changed)
        {
            var values = entry.CurrentValues;
            var scope = _map.ScopeProperty is not { } scopeProperty
                ? default!
                : NestedSetTypedValue<TScope>.Read(values, scopeProperty);

            if (!_scopes.TryGetValue(scope, out var scopedKeys))
            {
                scopedKeys = new HashSet<TKey>(_map.KeyComparer);
                _scopes.Add(scope, scopedKeys);
            }

            scopedKeys.Add(NestedSetTypedValue<TKey>.Read(values, _map.KeyProperty));
        }

        foreach (var (scope, keys) in _scopes)
        {
            var source = Nodes();
            var capabilities = NestedSetProviderCapabilities.Resolve(_context);

            if (_map.Scope is not null)
            {
                source = source.Where(MatchesValues(_map.ScopeProperty!, [scope]));
            }

            foreach (var batch in keys.Chunk(NestedSetBatch.MaximumRows))
            {
                // WHY: Scope participates in node identity. Filtering before key correlation supports tenant-local
                // NodeKeys while retaining the database's native collation and converter semantics.
                var matched =
                    _map.HasNativeKeyEquality
                    && batch.Length > 1
                    && capabilities.SupportsTrackedKeyCollection(_map.KeyProperty)
                        ? capabilities.MatchTrackedKeyCollection(source, _map.KeyProperty, batch)
                        : NestedSetTrackedRowset<TEntity>
                            .Match(source, _map.Key, batch)
                            .Select(row => row.Entity);

                var rows = await matched
                    .Select(row => new PersistedIdentity(
                        EF.Property<TKey>(row, _map.Key),
                        scope,
                        EF.Property<TTreeId>(row, _map.TreeId)))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (rows.Count != batch.Length)
                {
                    throw new DbUpdateConcurrencyException("A hierarchy node no longer exists.");
                }

                foreach (var row in rows)
                {
                    _identities.Add(row);
                    AddLockRequest(requests, row.Scope, row.TreeId);
                }
            }
        }

        await ResolveParentTargetsAsync(requests, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Resolves every requested destination inside the source node's immutable Scope.</summary>
    private async Task ResolveParentTargetsAsync(
        List<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    )
    {
        foreach (var group in _parentChanges.GroupBy(change => change.SourceScope, _scopes.Comparer))
        {
            var source = Nodes();
            var capabilities = NestedSetProviderCapabilities.Resolve(_context);

            if (_map.Scope is not null)
            {
                source = source.Where(MatchesValues(_map.ScopeProperty!, [group.Key]));
            }

            var changes = group.ToArray();
            foreach (var batch in changes.Chunk(NestedSetBatch.MaximumRows))
            {
                var parents = batch
                    .Select(change => change.TargetParent)
                    .ToArray();

                if (_map.HasNativeKeyEquality
                    && parents.Length > 1
                    && capabilities.SupportsTrackedKeyCollection(_map.KeyProperty))
                {
                    // WHY: Parent lock planning needs tree identities, not per-request ordinals. Deduplicating
                    // targets avoids a UNION branch for every move to the same parent.
                    var distinct = parents
                        .Distinct(_map.KeyComparer)
                        .ToArray();

                    var treeIds = await capabilities
                        .MatchTrackedKeyCollection(source, _map.KeyProperty, distinct)
                        .Select(node => EF.Property<TTreeId>(node, _map.TreeId))
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (treeIds.Length != distinct.Length)
                    {
                        throw new NestedSetException(
                            NestedSetErrorCode.NodeNotFound,
                            "Every requested Parent must resolve in the changed node's Scope.");
                    }

                    foreach (var treeId in treeIds)
                    {
                        AddLockRequest(requests, group.Key, treeId);
                    }

                    continue;
                }

                var targets = await NestedSetTrackedRowset<TEntity>
                    .Match(source, _map.Key, parents)
                    .Select(row => new TargetIdentity(row.Ordinal, EF.Property<TTreeId>(row.Entity, _map.TreeId)))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (targets.Count != batch.Length)
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.NodeNotFound,
                        "Every requested Parent must resolve in the changed node's Scope.");
                }

                foreach (var target in targets)
                {
                    AddLockRequest(requests, group.Key, target.TreeId);
                }
            }
        }
    }

    /// <summary>Adds one persisted tree to both the save-lock and tracked-refresh plans.</summary>
    private void AddLockRequest(
        List<INestedSetTreeLockRequest> requests,
        TScope scope,
        TTreeId treeId
    )
    {
        if (!_requestedTrees.TryGetValue(scope, out var treeIds))
        {
            // WHY: The CLR planning key must preserve representations that the database can distinguish.
            // Provider-native lock acquisition still resolves aliases equal under database collation.
            treeIds = new HashSet<TTreeId>(_treeIdComparer);
            _requestedTrees.Add(scope, treeIds);
        }

        if (!treeIds.Add(treeId))
        {
            return;
        }

        var request = new NestedSetTreeLockRequest<TTreeId, TScope>(
            _map.EntityType,
            scope,
            treeId,
            NestedSetTreeLockMode.Existing);

        // WHY: The global heterogeneous plan and this typed refresh plan share one immutable request object.
        // Rebuilding an erased save request would duplicate snapshots and lose the known identity types.
        requests.Add(request);
        _treeRequests.Add(request);
    }
}
