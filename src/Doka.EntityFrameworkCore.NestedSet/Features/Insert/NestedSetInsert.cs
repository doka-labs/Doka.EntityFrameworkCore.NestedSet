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

        var insertionEntries = new List<(EntityEntry Entry, NestedSetInsertionTracking.Identity? Identity)>();
        var insertionRelationships = new List<(EntityEntry Owner, INavigationBase Navigation, object Entity)>();
        var insertionValues = new List<NestedSetInsertionValues.Snapshot>();
        var (knownTracked, original, stagedKey, hasAssignedKey) = PrepareDetachedInsertion(
            entity,
            entry,
            insertionEntries,
            insertionRelationships,
            insertionValues);

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

                        TrackInsertionGraph(insertionEntries);
                        var stagedIdentity = insertionEntries[0].Identity!;

                        // WHY: This one insertion already owns its transaction, lock and structural assignment.
                        // The SaveChanges integration must not interpret it as an unauthorized direct tree addition.
                        using (var managedSave = NestedSetSaveChanges.EnterManagedSave(
                                   _store.Context,
                                   insertionEntries.Select(candidate => candidate.Entry.Entity),
                                   () => RequireSavedStage(
                                       entry,
                                       destination,
                                       stagedIdentity,
                                       hasAssignedKey,
                                       stagedKey,
                                       stagedScope,
                                       stagedTreeId,
                                       stagedParent,
                                       knownTracked),
                                   persistenceCompleted: () =>
                                   {
                                       foreach (var candidate in insertionEntries)
                                       {
                                           candidate.Identity?.Refresh();
                                       }
                                   }))
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

                        RequireSavedStage(
                            entry,
                            destination,
                            stagedIdentity,
                            hasAssignedKey,
                            stagedKey,
                            stagedScope,
                            stagedTreeId,
                            stagedParent,
                            knownTracked);
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

                            entry.Property<long>(_store.Map.LeftProperty)
                                .CurrentValue = saved.Left;
                            entry.Property<long>(_store.Map.RightProperty)
                                .CurrentValue = saved.Right;
                            entry.Property<int>(_store.Map.DepthProperty)
                                .CurrentValue = saved.Depth;
                            entry.Property<long>(_store.Map.PositionProperty)
                                .CurrentValue = saved.Position;

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

                        // WHY: Application state callbacks can reject detachment. Complete this lifecycle while
                        // the transaction or caller savepoint can still roll back the INSERT and generated values.
                        DetachInsertionGraph(insertionEntries);
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
                Restore(entry, insertionEntries, insertionRelationships, original, insertionValues);
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
    }

    /// <summary>Captures the detached aggregate and releases exact references when application reads fail.</summary>
    /// <param name="entity">The detached input root whose owned graph is bounded by its navigation metadata.</param>
    /// <param name="entry">An input already proven detached, so caller-owned tracked entries are never cleaned.</param>
    /// <param name="introduced">The exact newly introduced entries owned by this attempt.</param>
    /// <param name="relationships">The caller's original owned aggregate memberships.</param>
    /// <param name="insertionValues">Generated leaves and owned identities restored after a later rollback.</param>
    /// <returns>The caller baseline and independent structural identity snapshots.</returns>
    private (HashSet<object> Tracked, Dictionary<IProperty, object?> Original, TKey Key, bool Assigned)
        PrepareDetachedInsertion(
            TEntity entity,
            EntityEntry entry,
            List<(EntityEntry Entry, NestedSetInsertionTracking.Identity? Identity)> introduced,
            List<(EntityEntry Owner, INavigationBase Navigation, object Entity)> relationships,
            List<NestedSetInsertionValues.Snapshot> insertionValues
        )
    {
        try
        {
            // WHY: Baseline discovery and application snapshot getters can create owned Detached references
            // before failing. Retain the bounded graph before those reads so initialization owns every new entry.
            CollectInsertionGraph(entity, introduced, relationships);
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
            // values may be null or sentinels; rollback preserves them exactly while stage identities stay typed.
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

            // WHY: The existing rollback snapshot already isolates mutable assigned keys from caller storage.
            // Sentinel keys may be generated by EF; only an assigned identity is immutable during the save.
            var stagedKey = (TKey)original[_store.Map.KeyProperty]!;
            var hasAssignedKey = _store.Map.KeyProperty.ValueGenerated == ValueGenerated.Never
                || !_store
                    .Map
                    .KeyProperty
                    .GetValueComparer()
                    .Equals(stagedKey, _store.Map.KeyProperty.Sentinel);

            var structuralProperties = original.Keys.ToHashSet();

            foreach (var candidate in introduced)
            {
                // WHY: Generated leaves and owned identities can propagate before a late failure. Share their
                // metadata-qualified policy with bulk rollback without copying structural roles twice.
                if (NestedSetInsertionValues.Capture(
                        candidate.Entry.Metadata,
                        candidate.Entry.Entity,
                        ReferenceEquals(candidate.Entry.Entity, entity) ? structuralProperties : null) is { } snapshot)
                {
                    insertionValues.Add(snapshot);
                }
            }

            return (knownTracked, original, stagedKey, hasAssignedKey);
        }
        catch (Exception initializationError)
        {
            List<Exception>? cleanupErrors = null;

            try
            {
                DetachInsertionGraph(introduced);
            }
            catch (Exception cleanupError)
            {
                (cleanupErrors ??= []).Add(cleanupError);
            }

            try
            {
                // WHY: Root entry creation precedes traversal. Even a still-Detached failure owns its native
                // reference if traversal failed before delivering the first callback.
                NestedSetInsertionTracking.Detach(entry);
            }
            catch (Exception cleanupError)
            {
                (cleanupErrors ??= []).Add(cleanupError);
            }

            try
            {
                NestedSetInsertionTracking.VerifyFailedCleanup(introduced.Select(candidate => candidate.Entry));
            }
            catch (Exception cleanupError)
            {
                (cleanupErrors ??= []).Add(cleanupError);
            }

            if (cleanupErrors is not null)
            {
                throw new AggregateException(
                    "Nested-set insertion initialization cleanup failed. Discard the context.",
                    [initializationError, .. cleanupErrors]);
            }

            throw;
        }
    }

    /// <summary>Retains the complete bounded owned graph before its first tracking transition can fail.</summary>
    /// <param name="entity">The detached root and owned payload aggregate.</param>
    /// <param name="introduced">The exact entries retained for rollback and native identity cleanup.</param>
    /// <param name="relationships">The owned navigation memberships restored after a failed insertion.</param>
    private void CollectInsertionGraph(
        TEntity entity,
        List<(EntityEntry Entry, NestedSetInsertionTracking.Identity? Identity)> introduced,
        List<(EntityEntry Owner, INavigationBase Navigation, object Entity)> relationships
    )
    {
        // WHY: TrackGraph visits only the detached aggregate. A tracker-wide before/after scan would allocate
        // wrappers for unrelated application entities on every single insert.
        _store.Context.ChangeTracker.TrackGraph(
            entity,
            new HashSet<object>(ReferenceEqualityComparer.Instance),
            graphNode =>
            {
                var candidate = graphNode.Entry;

                if (!graphNode.NodeState.Add(candidate.Entity))
                {
                    return false;
                }

                if (candidate.State == EntityState.Detached)
                {
                    // WHY: Traversal creates a native reference before invoking this callback. Own newly
                    // detached foreign entries before rejecting them, preserving previously tracked caller rows.
                    introduced.Add((candidate, null));
                }

                if (!ReferenceEquals(candidate.Entity, entity)
                    && !candidate.Metadata.IsOwned())
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidContext,
                        "Insert a node without populated non-owned navigation properties.");
                }

                if (candidate.State != EntityState.Detached)
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidContext,
                        "The inserted node and its owned payload must be detached.");
                }

                if (graphNode is { SourceEntry: { } owner, InboundNavigation: { } navigation })
                {
                    relationships.Add((owner, navigation, candidate.Entity));
                }

                return true;
            });
    }

    /// <summary>Installs identities only after the complete detached aggregate has been retained.</summary>
    /// <param name="introduced">The exact entries collected before any structural or tracking assignment.</param>
    private static void TrackInsertionGraph(
        List<(EntityEntry Entry, NestedSetInsertionTracking.Identity? Identity)> introduced
    )
    {
        // WHY: A root's first transition can create native Detached references to owned payload before its
        // traversal callback runs. Collect the full graph first, then own every partial transition and map identity.
        for (var index = 0; index < introduced.Count; index++)
        {
            var candidate = introduced[index].Entry;
            candidate.State = EntityState.Added;
            introduced[index] = (candidate, NestedSetInsertionTracking.Capture(candidate));
        }
    }

    /// <summary>Attempts exact lifecycle cleanup for every introduced entry without abandoning owned payload.</summary>
    /// <param name="entries">Only the entries introduced for this bounded insertion aggregate.</param>
    private static void DetachInsertionGraph(
        IReadOnlyList<(EntityEntry Entry, NestedSetInsertionTracking.Identity? Identity)> entries
    )
    {
        List<Exception>? failures = null;

        foreach (var (_, identity) in entries)
        {
            try
            {
                // WHY: Mutable principal keys must match their installed identities before public detachment
                // performs owned relationship fixup. Prepare the whole aggregate before detaching any entry.
                identity?.PrepareDetach();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        for (var index = entries.Count - 1; index >= 0; index--)
        {
            try
            {
                if (entries[index].Identity is { } identity)
                {
                    identity.Detach();
                }
                else
                {
                    NestedSetInsertionTracking.Detach(entries[index].Entry);
                }
            }
            catch (Exception error)
            {
                // WHY: An application lifecycle callback must not prevent cleanup of the remaining aggregate.
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("The inserted aggregate could not be completely detached.", failures);
        }
    }

    /// <summary>Rejects callbacks that alter the exact staged structure or add another hierarchy entity.</summary>
    /// <param name="entry">The introduced root whose current structural values remain staged.</param>
    /// <param name="destination">The placement resolved under the exact tree lock.</param>
    /// <param name="identity">The installed temporary or provider-generated identity captured before callbacks.</param>
    /// <param name="hasAssignedKey">Whether the caller supplied an identity before insertion.</param>
    /// <param name="key">The independent original assigned-key snapshot.</param>
    /// <param name="scope">The exact expected scope snapshot.</param>
    /// <param name="treeId">The exact expected tree identity snapshot.</param>
    /// <param name="parent">The exact expected parent snapshot.</param>
    /// <param name="knownTracked">The caller's earlier tracked hierarchy instances.</param>
    private void RequireSavedStage(
        EntityEntry<TEntity> entry,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination,
        NestedSetInsertionTracking.Identity identity,
        bool hasAssignedKey,
        TKey key,
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
            || !identity.Matches(_store.Map.KeyProperty)
            || (hasAssignedKey
                && !NestedSetTypedValue<TKey>.Matches(
                    _store.Map.KeyProperty,
                    NestedSetTypedValue<TKey>.Read(values, _store.Map.KeyProperty),
                    key))
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
    /// <param name="entry">The original detached root entry used to restore its mapped structure.</param>
    /// <param name="insertionEntries">The exact aggregate entries owned by this insertion attempt.</param>
    /// <param name="insertionRelationships">The caller's original owned navigation memberships.</param>
    /// <param name="original">The independent snapshots of every caller-visible structural role.</param>
    /// <param name="insertionValues">The independent generated-leaf and owned-identity snapshots.</param>
    private static void Restore(
        EntityEntry entry,
        IReadOnlyList<(EntityEntry Entry, NestedSetInsertionTracking.Identity? Identity)> insertionEntries,
        IReadOnlyList<(EntityEntry Owner, INavigationBase Navigation, object Entity)> insertionRelationships,
        Dictionary<IProperty, object?> original,
        IReadOnlyList<NestedSetInsertionValues.Snapshot> insertionValues
    )
    {
        List<Exception>? failures = null;

        try
        {
            DetachInsertionGraph(insertionEntries);

            if (!insertionEntries.Any(candidate => ReferenceEquals(candidate.Entry.Entity, entry.Entity))
                || entry.State != EntityState.Detached)
            {
                NestedSetInsertionTracking.Detach(entry);
            }
        }
        catch (Exception error)
        {
            (failures ??= []).Add(error);
        }

        try
        {
            // WHY: Repeated callback-owned mutable rekeys can corrupt native hash slots. A failed attempt
            // audits only maps touched by its exact entries; any residual identity requires discarding the context.
            NestedSetInsertionTracking.VerifyFailedCleanup(insertionEntries.Select(candidate => candidate.Entry));
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

        foreach (var (owner, navigation, entity) in insertionRelationships)
        {
            try
            {
                // WHY: Detaching a mutable-key aggregate can sever its owned CLR relationships. Restore the
                // caller's original graph after every entry is detached so retry can save the same owned payload.
                NestedSetInsertionTracking.RestoreRelationship(owner.Entity, navigation, entity);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        foreach (var snapshot in insertionValues)
        {
            try
            {
                snapshot.Restore();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        if (entry.State != EntityState.Detached)
        {
            try
            {
                NestedSetInsertionTracking.Detach(entry);
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
