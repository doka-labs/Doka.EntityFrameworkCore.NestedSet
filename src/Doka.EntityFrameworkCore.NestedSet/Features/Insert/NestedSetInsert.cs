namespace Doka.EntityFrameworkCore.NestedSet.Features.Insert;

/// <summary>Inserts one node into an exact tree under its registry lock and atomic execution boundary.</summary>
/// <typeparam name="TEntity">The mapped domain entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TTreeId">The mapped tree identity type.</typeparam>
/// <typeparam name="TScope">The scope type.</typeparam>
internal sealed class NestedSetInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> _executor;
    private readonly NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope> _placement;

    /// <summary>Connects the algorithm to its exact-tree store and execution boundary.</summary>
    /// <param name="store">The shared tree-bound queries and structural writes.</param>
    /// <param name="executor">The transaction and lock coordinator.</param>
    /// <param name="placement">The destination resolver shared with other mutation algorithms.</param>
    internal NestedSetInsert(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> executor,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope> placement
    )
    {
        _store = store;
        _executor = executor;
        _placement = placement;
    }

    /// <summary>Creates the sole root of the store's new tree.</summary>
    /// <param name="entity">The detached root whose managed structure is assigned during insertion.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertRootAsync(
        TEntity entity,
        CancellationToken cancellationToken = default
    ) => InsertAsync(entity, default, null, cancellationToken, automatic: true);

    /// <summary>Inserts a child using the configured automatic rule or the last sibling position.</summary>
    /// <param name="entity">The detached child whose managed structure is assigned during insertion.</param>
    /// <param name="parent">The existing parent key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertChildAsync(
        TEntity entity,
        TKey parent,
        CancellationToken cancellationToken = default
    ) => InsertAsync(entity, parent, NestedSetPlacement.LastChild, cancellationToken, automatic: true);

    /// <summary>Inserts a child at the first sibling position when manual placement is permitted.</summary>
    /// <param name="entity">The detached child to insert.</param>
    /// <param name="parent">The existing parent key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertAsFirstChildAsync(
        TEntity entity,
        TKey parent,
        CancellationToken cancellationToken = default
    ) => InsertAsync(entity, parent, NestedSetPlacement.FirstChild, cancellationToken);

    /// <summary>Inserts a child at the last sibling position when manual placement is permitted.</summary>
    /// <param name="entity">The detached child to insert.</param>
    /// <param name="parent">The existing parent key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertAsLastChildAsync(
        TEntity entity,
        TKey parent,
        CancellationToken cancellationToken = default
    ) => InsertAsync(entity, parent, NestedSetPlacement.LastChild, cancellationToken);

    /// <summary>Inserts a sibling immediately before an existing non-root node.</summary>
    /// <param name="entity">The detached sibling to insert.</param>
    /// <param name="sibling">The existing sibling key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertBeforeAsync(
        TEntity entity,
        TKey sibling,
        CancellationToken cancellationToken = default
    ) => InsertAsync(entity, sibling, NestedSetPlacement.Before, cancellationToken);

    /// <summary>Inserts a sibling immediately after an existing non-root node.</summary>
    /// <param name="entity">The detached sibling to insert.</param>
    /// <param name="sibling">The existing sibling key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertAfterAsync(
        TEntity entity,
        TKey sibling,
        CancellationToken cancellationToken = default
    ) => InsertAsync(entity, sibling, NestedSetPlacement.After, cancellationToken);

    /// <summary>Inserts a detached node and restores its CLR state if the write fails.</summary>
    /// <param name="entity">The new node without populated navigations.</param>
    /// <param name="anchor">The destination parent or sibling key; ignored when appending a root.</param>
    /// <param name="placement">The anchor-relative placement, or null to append a root.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and all database work.</param>
    /// <param name="automatic">Whether a configured rule chooses the final sibling position.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    private async Task InsertAsync(
        TEntity entity,
        TKey? anchor,
        NestedSetPlacement? placement,
        CancellationToken cancellationToken,
        bool automatic = false
    )
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (!automatic)
        {
            NestedSetOrderer<TEntity, TKey, TTreeId, TScope>.RequireManualPlacement(_store.Map.Order);
        }

        var entry = _store.Entry(entity);

        if (entry.State != EntityState.Detached)
        {
            throw new NestedSetException(NestedSetErrorCode.InvalidContext, "The inserted node must be detached.");
        }

        var tracker = _store.Context.ChangeTracker;
        var automaticDetection = tracker.AutoDetectChangesEnabled;
        HashSet<object> knownTracked;

        try
        {
            // WHY: This baseline needs identity only. The executor detects pending CLR changes before any write;
            // running it here would detect the entire tracker and wrap every unrelated application entity.
            tracker.AutoDetectChangesEnabled = false;
            knownTracked = NestedSetEntityAccess<TEntity>
                .Entries(_store.Context, _store.Map.EntityType)
                .Select(candidate => (object)candidate.Entity)
                .ToHashSet(ReferenceEqualityComparer.Instance);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = automaticDetection;
        }

        // WHY: Owned payload belongs to the same aggregate row lifecycle. Other populated relationships could add
        // entities whose structural coordinates were never assigned by this operation.
        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue is { } value
                && (value is not IEnumerable sequence
                    || sequence
                        .Cast<object>()
                        .Any())
                && !navigation.Metadata.TargetEntityType.IsOwned())
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidContext,
                    "Insert a node without populated non-owned navigation properties.");
            }
        }

        // WHY: Database rollback does not restore caller CLR values or generated keys. These original metadata
        // values may be null or sentinels; rollback preserves them exactly while active stage identities stay typed.
        var structural = new List<IProperty>
        {
            _store.Map.KeyProperty,
            _store.Map.LeftProperty,
            _store.Map.RightProperty,
            _store.Map.TreeIdProperty,
            _store.Map.ParentProperty,
            _store.Map.DepthProperty,
            _store.Map.PositionProperty,
        };

        if (_store.Map.ScopeProperty is { } scopeProperty)
        {
            structural.Add(scopeProperty);
        }

        var originalValues = entry.CurrentValues;
        var original = structural.ToDictionary(
            property => property,
            property => NestedSetStructuralValue.Snapshot(property, originalValues[property]));

        EntityEntry[] insertionEntries = [];
        NestedSetTrackerSnapshot? callbackWrites = null;

        try
        {
            await _store
                .ExecuteMutationAsync(
                    _executor,
                    async token =>
                    {
                        if (placement is null
                            && await _store
                                .Nodes
                                .AnyAsync(token)
                                .ConfigureAwait(false))
                        {
                            throw new NestedSetException(
                                NestedSetErrorCode.InvalidStructure,
                                "A TreeId can contain exactly one root.");
                        }

                        // WHY: A root's exact tree was proven empty under its reserved registry lock above.
                        // Existing-tree placement derives its interval from the anchor without a maximum scan.
                        const long maximum = 0;

                        // WHY: Reject overflow before shifting even when the insertion anchor itself fits. An indexed
                        // threshold probe avoids a full maximum aggregation for non-root insertions.
                        if (placement is not null
                            && await _store
                                .Nodes
                                .AnyAsync(node => EF.Property<long>(node, _store.Map.Right) > long.MaxValue - 2, token)
                                .ConfigureAwait(false))
                        {
                            throw new OverflowException("The insertion exceeds the supported 64-bit coordinates.");
                        }

                        _ = checked(maximum + 2);

                        var destination = await _placement
                            .ResolveAsync(anchor, placement, maximum, token)
                            .ConfigureAwait(false);

                        // WHY: A leaf needs two adjacent coordinates; existing nodes and siblings vacate them first.
                        if (placement is not null)
                        {
                            await _store
                                .ShiftBoundsAsync(destination.Boundary, 2, token)
                                .ConfigureAwait(false);
                        }

                        if (placement is not null and not NestedSetPlacement.LastChild)
                        {
                            await _store
                                .ChangeLongAsync(
                                    _store
                                        .Siblings(destination.Parent)
                                        .Where(node =>
                                            EF.Property<long>(node, _store.Map.Position) >= destination.Position),
                                    _store.Map.Position,
                                    1,
                                    1,
                                    token)
                                .ConfigureAwait(false);
                        }

                        var stagedScope = _store.Map.ScopeProperty is { } mappedScope
                            ? NestedSetTypedValue<TScope>.Snapshot(mappedScope, _store.Scope)
                            : _store.Scope;

                        var stagedTreeId = NestedSetTypedValue<TTreeId>.Snapshot(
                            _store.Map.TreeIdProperty,
                            _store.TreeId);

                        var stagedParent = destination.Parent.Snapshot(_store.Map.ParentProperty);

                        // WHY: Mutable scope or parent values need separate snapshots for expected structure and
                        // assignment; a callback must not mutate the expectation or the store's own scope array.
                        if (_store.Map.ScopeProperty is { } scope)
                        {
                            NestedSetStructuralValue.Assign(
                                entry,
                                scope,
                                NestedSetTypedValue<TScope>.Snapshot(scope, stagedScope));
                        }

                        NestedSetStructuralValue.Assign(
                            entry,
                            _store.Map.TreeIdProperty,
                            NestedSetTypedValue<TTreeId>.Snapshot(_store.Map.TreeIdProperty, stagedTreeId));

                        NestedSetStructuralValue.Assign(
                            entry,
                            _store.Map.ParentProperty,
                            stagedParent.Snapshot(_store.Map.ParentProperty)
                                .BoxedValue);

                        NestedSetStructuralValue.Assign(entry, _store.Map.DepthProperty, destination.Depth);
                        NestedSetStructuralValue.Assign(entry, _store.Map.PositionProperty, destination.Position);
                        NestedSetStructuralValue.Assign(entry, _store.Map.LeftProperty, destination.Boundary);
                        NestedSetStructuralValue.Assign(
                            entry,
                            _store.Map.RightProperty,
                            checked(destination.Boundary + 1));

                        insertionEntries = TrackInsertionGraph(entity);

                        // WHY: This one insertion already owns its transaction, lock and structural assignment.
                        // The SaveChanges integration must not interpret it as an unauthorized direct tree addition.
                        using (var managedSave = NestedSetSaveChanges.EnterManagedSave(
                                   _store.Context,
                                   insertionEntries.Select(candidate => candidate.Entity),
                                   () => RequireSavedStage(
                                       entry,
                                       destination,
                                       stagedScope,
                                       stagedTreeId,
                                       stagedParent,
                                       knownTracked)))
                        {
                            var autoSavepoints = _store.Context.Database.AutoSavepointsEnabled;

                            try
                            {
                                // WHY: The mutation coordinator already owns rollback for this save. EF's additional
                                // savepoint would duplicate that protection and add commands while the lock is held.
                                _store.Context.Database.AutoSavepointsEnabled = false;
                                await _store
                                    .Context
                                    .SaveChangesAsync(token)
                                    .ConfigureAwait(false);
                            }
                            finally
                            {
                                _store.Context.Database.AutoSavepointsEnabled = autoSavepoints;
                                callbackWrites = managedSave.CallbackWrites;
                            }

                            managedSave.RequirePersisted();
                        }

                        RequireSavedStage(entry, destination, stagedScope, stagedTreeId, stagedParent, knownTracked);
                        NestedSetTelemetry.RecordRowsAffected(1);

                        if (automatic
                            && _store.Map.Order is not null
                            && destination.Position > 0)
                        {
                            // WHY: Automatic insertion appends initially. Position zero proves the sibling group was
                            // empty under the lock; a single node already satisfies every configured ordering rule.
                            var key = NestedSetTypedValue<TKey>.Read(entry, _store.Map.KeyProperty);
                            await new NestedSetOrderer<TEntity, TKey, TTreeId, TScope>(_store)
                                .ReorderAsync(key, token)
                                .ConfigureAwait(false);

                            // WHY: Generated keys and sort values exist only after saving. The subsequent set-based
                            // move bypasses tracking, so return the node with its final persisted coordinates.
                            var saved = await _store
                                .FindAsync(key, token)
                                .ConfigureAwait(false);

                            entry.Property<long>(_store.Map.LeftProperty).CurrentValue = saved.Left;
                            entry.Property<long>(_store.Map.RightProperty).CurrentValue = saved.Right;
                            entry.Property<int>(_store.Map.DepthProperty).CurrentValue = saved.Depth;
                            entry.Property<long>(_store.Map.PositionProperty).CurrentValue = saved.Position;

                            // WHY: The move can fire update triggers or change a provider-generated version after
                            // INSERT returned its token. Returning that stale token would break a later attached save.
                            await NestedSetGeneratedConcurrency<TEntity>
                                .RefreshAsync(_store.Nodes.Where(_store.Equal(_store.Map.Key, key)), entry, token)
                                .ConfigureAwait(false);
                        }

                        if (placement is not null)
                        {
                            await NestedSetTreeRegistryState
                                .TouchAsync(_store.Context, _store.LockRequest(NestedSetTreeLockMode.Existing), token)
                                .ConfigureAwait(false);
                        }
                    },
                    placement is null ? NestedSetTreeLockMode.New : NestedSetTreeLockMode.Existing,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception operationError)
        {
            List<Exception>? restoreErrors = null;

            try
            {
                Restore(entry, insertionEntries, original);
            }
            catch (Exception restoreError)
            {
                (restoreErrors ??= []).Add(restoreError);
            }

            try
            {
                // WHY: EF accepted ordinary callback writes with the INSERT that the transaction rolled back.
                // Their pending state must not claim that the database still contains those rows.
                callbackWrites?.Restore();
            }
            catch (Exception restoreError)
            {
                (restoreErrors ??= []).Add(restoreError);
            }

            if (restoreErrors is not null)
            {
                throw new AggregateException(
                    "Nested-set insertion restoration failed. Discard the context.",
                    [operationError, .. restoreErrors]);
            }

            throw;
        }

        // WHY: Keeping this instance tracked would conflict with later set-based hierarchy updates.
        // Failure cleanup performs its own protected detachment; a finally here could replace the original error.
        DetachInsertionGraph(insertionEntries);
    }

    /// <summary>Tracks the root and its owned payload while rejecting other newly discovered graph entries.</summary>
    private EntityEntry[] TrackInsertionGraph(
        TEntity entity
    )
    {
        var introduced = new List<EntityEntry>();

        try
        {
            // WHY: TrackGraph visits only the detached aggregate. A tracker-wide before/after scan would allocate
            // wrappers for unrelated application entities on every single insert.
            _store.Context.ChangeTracker.TrackGraph(
                entity,
                graphNode =>
                {
                    var candidate = graphNode.Entry;

                    if (!ReferenceEquals(candidate.Entity, entity)
                        && !candidate.Metadata.IsOwned())
                    {
                        throw new NestedSetException(
                            NestedSetErrorCode.InvalidContext,
                            "Insert a node without populated non-owned navigation properties.");
                    }

                    introduced.Add(candidate);
                    candidate.State = EntityState.Added;
                });
        }
        catch
        {
            // WHY: A graph callback can fail after some entries became tracked. Undo only this aggregate so the
            // caller's earlier tracked work retains its original state.
            DetachInsertionGraph(introduced);

            throw;
        }

        return introduced.ToArray();
    }

    /// <summary>Detaches every entry introduced for one successful aggregate insertion.</summary>
    private static void DetachInsertionGraph(
        IReadOnlyList<EntityEntry> entries
    )
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            entries[index].State = EntityState.Detached;
        }
    }

    /// <summary>Rejects callbacks that alter the exact staged structure or add another hierarchy entity.</summary>
    private void RequireSavedStage(
        EntityEntry<TEntity> entry,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination,
        TScope scope,
        TTreeId treeId,
        NestedSetParent<TKey> parent,
        HashSet<object> knownTracked
    )
    {
        var values = entry.CurrentValues;

        // WHY: Managed saves bypass ordinary direct-write guards. Validate before ordering or refresh can hide
        // a callback's structural edit, using representation equality rather than a broader application comparer.
        if (entry.State == EntityState.Detached
            || HasUnexpectedHierarchyEntry(entry, knownTracked)
            || (_store.Map.ScopeProperty is { } scopeProperty
                && !NestedSetTypedValue<TScope>.Matches(
                    scopeProperty,
                    NestedSetTypedValue<TScope>.Read(values, scopeProperty),
                    scope))
            || !NestedSetTypedValue<TTreeId>.Matches(
                _store.Map.TreeIdProperty,
                NestedSetTypedValue<TTreeId>.Read(values, _store.Map.TreeIdProperty),
                treeId)
            || !NestedSetParent<TKey>
                .Read(values, _store.Map.ParentProperty)
                .Matches(_store.Map.ParentProperty, parent)
            || NestedSetTypedValue<long>.Read(values, _store.Map.LeftProperty) != destination.Boundary
            || NestedSetTypedValue<long>.Read(values, _store.Map.RightProperty) != checked(destination.Boundary + 1)
            || NestedSetTypedValue<int>.Read(values, _store.Map.DepthProperty) != destination.Depth
            || NestedSetTypedValue<long>.Read(values, _store.Map.PositionProperty) != destination.Position)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidStructure,
                "Save callbacks must not alter managed hierarchy values or tracked nodes during an insertion.");
        }
    }

    /// <summary>Finds hierarchy entries introduced by callbacks without repeating global change detection.</summary>
    private bool HasUnexpectedHierarchyEntry(
        EntityEntry root,
        HashSet<object> knownTracked
    )
    {
        var tracker = _store.Context.ChangeTracker;
        var automaticDetection = tracker.AutoDetectChangesEnabled;

        try
        {
            // WHY: The save guard already detects callback changes before SQL. After the save, only new tracked
            // identities matter here; property values on the root are checked separately below.
            tracker.AutoDetectChangesEnabled = false;

            return NestedSetEntityAccess<TEntity>
                .Entries(_store.Context, _store.Map.EntityType)
                .Any(candidate => !ReferenceEquals(candidate.Entity, root.Entity)
                    && !knownTracked.Contains(candidate.Entity));
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = automaticDetection;
        }
    }

    /// <summary>Attempts all owned cleanup steps while retaining every application callback failure.</summary>
    private static void Restore(
        EntityEntry entry,
        IReadOnlyList<EntityEntry> insertionEntries,
        Dictionary<IProperty, object?> original
    )
    {
        List<Exception>? failures = null;

        try
        {
            DetachInsertionGraph(insertionEntries);

            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }
        catch (Exception error)
        {
            (failures ??= []).Add(error);
        }

        foreach (var (property, value) in original)
        {
            try
            {
                // WHY: EF's ordinary setter can skip a comparer-equal alias or clone. Restore exact mapped CLR
                // values, including independent mutable keys, after removing this operation's tracked entry.
                NestedSetStructuralValue.Assign(entry, property, NestedSetStructuralValue.Snapshot(property, value));
            }
            catch (Exception error)
            {
                // WHY: One rejecting setter must not prevent restoration of the remaining managed properties.
                (failures ??= []).Add(error);
            }
        }

        if (entry.State != EntityState.Detached)
        {
            try
            {
                entry.State = EntityState.Detached;
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("The inserted entity could not be completely restored.", failures);
        }
    }
}
