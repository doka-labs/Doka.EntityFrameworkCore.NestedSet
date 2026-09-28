namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

/// <summary>Imports one root branch or subtree using an exact-tree interval and bounded insertion saves.</summary>
/// <typeparam name="TEntity">The detached hierarchy entity type.</typeparam>
/// <typeparam name="TKey">The mapped primary key type.</typeparam>
/// <typeparam name="TTreeId">The mapped tree identity type.</typeparam>
/// <typeparam name="TScope">The mapped scope type.</typeparam>
internal sealed class NestedSetBulkInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> _executor;
    private readonly NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope> _placement;

    /// <summary>Shares the exact-tree mutation lock, transaction contract and destination resolver.</summary>
    /// <param name="store">The typed tree binding and structural persistence operations.</param>
    /// <param name="executor">The transaction and registry lock coordinator.</param>
    /// <param name="placement">The destination resolver shared with the other mutation algorithms.</param>
    internal NestedSetBulkInsert(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> executor,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope> placement
    )
    {
        _store = store;
        _executor = executor;
        _placement = placement;
    }

    /// <summary>Appends an entire input branch beneath an existing parent in the store's tree.</summary>
    /// <param name="subtree">The detached branch whose topology determines the inserted descendants.</param>
    /// <param name="parent">The existing parent key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for input validation and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertSubtreeAsync(
        NestedSetBranch<TEntity> subtree,
        TKey parent,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(subtree);
        ArgumentNullException.ThrowIfNull(parent);

        return InsertAsync([subtree], parent, cancellationToken);
    }

    /// <summary>Validates the complete input before starting one transaction and bounded insertion waves.</summary>
    private async Task InsertAsync(
        IReadOnlyList<NestedSetBranch<TEntity>> roots,
        TKey parent,
        CancellationToken cancellationToken
    )
    {
        var plan = Prepare(roots, cancellationToken);

        var callbackWrites = new NestedSetCallbackWrites();

        try
        {
            await _executor
                .ExecuteAsync(
                    token => InsertPreparedAsync(plan, parent, callbackWrites, token),
                    [_store.LockRequest(NestedSetTreeLockMode.Existing)],
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception operationError)
        {
            List<Exception>? restoreErrors = null;

            // WHY: Transaction rollback cannot undo generated keys, trigger values or mapped CLR assignments.
            try
            {
                plan.Restore();
            }
            catch (Exception restoreError)
            {
                // WHY: A setter, comparer, or state callback can also fail during cleanup. Preserve the original
                // failure, including cancellation or rollback errors, rather than replacing it with that callback.
                (restoreErrors ??= []).Add(restoreError);
            }

            try
            {
                callbackWrites.Restore();
            }
            catch (Exception restoreError)
            {
                (restoreErrors ??= []).Add(restoreError);
            }

            if (restoreErrors is not null)
            {
                throw new AggregateException(
                    "Nested-set import restoration failed. Discard the context.",
                    [operationError, .. restoreErrors]);
            }

            throw;
        }

        // WHY: Refresh setters may run application code; preserve the successful operation's final detach without
        // a finally block that could replace an earlier operation or restoration exception.
        plan.Detach();
    }

    /// <summary>Validates and snapshots one import before any transaction or database write begins.</summary>
    /// <param name="roots">The detached root branches whose topology is validated before any write.</param>
    /// <param name="cancellationToken">The token checked before tracker capture and throughout input traversal.</param>
    /// <returns>The typed tree plan with compact rollback state.</returns>
    private NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> Prepare(
        IReadOnlyList<NestedSetBranch<TEntity>> roots,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Prepare(
            roots,
            NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>.CaptureKnownTracked(
                _store.Context,
                _store.Map.EntityType),
            cancellationToken);
    }

    /// <summary>Prepares one tree while sharing the forest's immutable tracked identity baseline.</summary>
    /// <param name="roots">The detached root branches whose topology is validated before any write.</param>
    /// <param name="knownTracked">The identity baseline shared by every tree in the operation.</param>
    /// <param name="cancellationToken">The token checked throughout input traversal.</param>
    /// <returns>The typed tree plan with compact rollback state.</returns>
    internal NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> Prepare(
        IReadOnlyList<NestedSetBranch<TEntity>> roots,
        HashSet<TEntity> knownTracked,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(roots);
        cancellationToken.ThrowIfCancellationRequested();

        return new NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>(_store, roots, knownTracked, cancellationToken);
    }

    /// <summary>Executes a previously validated plan while the caller owns every required tree lock.</summary>
    /// <param name="plan">The validated import whose structural input values the caller restores on failure.</param>
    /// <param name="parent">The destination parent key within the store's existing tree.</param>
    /// <param name="callbackWrites">Receives accepted callback writes that the caller restores on failure.</param>
    /// <param name="cancellationToken">The token used for all database work and batch staging.</param>
    private async Task InsertPreparedAsync(
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> plan,
        TKey parent,
        NestedSetCallbackWrites callbackWrites,
        CancellationToken cancellationToken
    )
    {
        var destination = await PrepareDestinationAsync(plan, parent, NestedSetPlacement.LastChild, cancellationToken)
            .ConfigureAwait(false);

        // WHY: Unresolved parents stay null until every generated identity exists. Assigned links use EF
        // dependency ordering only when doing so cannot mutate caller navigation graphs.
        var autoSavepoints = _store.Context.Database.AutoSavepointsEnabled;

        try
        {
            // WHY: The mutation coordinator already owns the complete rollback boundary. Per-batch EF savepoints
            // would add database work without creating an independently recoverable unit.
            _store.Context.Database.AutoSavepointsEnabled = false;

            if (_store.Map.Order is null)
            {
                plan.PrepareGeometry(destination, null, cancellationToken);
            }

            for (var offset = 0; offset < plan.Nodes.Count; offset += NestedSetBatch.MaximumRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var count = Math.Min(NestedSetBatch.MaximumRows, plan.Nodes.Count - offset);
                plan.StageBatch(destination, offset, count, cancellationToken);

                using (var managedSave = NestedSetSaveChanges.EnterManagedSave(
                           _store.Context,
                           plan.ManagedEntities,
                           plan.RequireSavedStage))
                {
                    try
                    {
                        await _store
                            .Context
                            .SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        callbackWrites.Add(managedSave.CallbackWrites);
                    }

                    managedSave.RequirePersisted();
                }

                plan.RequireSavedStage();
                NestedSetTelemetry.RecordRowsAffected(count);
                NestedSetTelemetry.RecordBatch();
                plan.CompleteBatch();
            }
        }
        finally
        {
            _store.Context.Database.AutoSavepointsEnabled = autoSavepoints;
        }

        await FinalizePreparedAsync(plan, destination, NestedSetPlacement.LastChild, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reserves and validates one tree interval while the caller owns its registry lock.</summary>
    /// <param name="plan">The validated detached branch and its compact structural snapshot.</param>
    /// <param name="parent">The existing parent key, or default for a new root.</param>
    /// <param name="placement">The destination placement, or null for a new root.</param>
    /// <param name="cancellationToken">The token used for database reads and interval reservation.</param>
    /// <returns>The locked destination whose geometry is shared by all insertion waves.</returns>
    internal async Task<NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination> PrepareDestinationAsync(
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> plan,
        TKey? parent,
        NestedSetPlacement? placement,
        CancellationToken cancellationToken
    )
    {
        if (placement is null
            && (plan.Roots.Count != 1
                || await _store
                    .Nodes
                    .AnyAsync(cancellationToken)
                    .ConfigureAwait(false)))
        {
            throw new NestedSetException(NestedSetErrorCode.InvalidStructure, "A TreeId can contain exactly one root.");
        }

        // WHY: A new root's exact tree is empty under its reserved registry lock. Only a child import needs
        // the existing maximum to verify capacity and whether its reserved interval shifts existing nodes.
        var maximum = placement is null
            ? 0
            : await _store
                .MaximumAsync(cancellationToken)
                .ConfigureAwait(false);

        _ = checked(maximum + plan.Width);

        var destination = await _placement
            .ResolveAsync(parent, placement, maximum, cancellationToken)
            .ConfigureAwait(false);

        plan.RequireCapacity(destination);

        // WHY: Appending a new tree cannot affect existing coordinates. Inserting beneath a parent shifts the
        // remaining tree exactly once, independently of the imported node count.
        if (destination.Boundary <= maximum)
        {
            await _store
                .ShiftBoundsAsync(destination.Boundary, plan.Width, cancellationToken)
                .ConfigureAwait(false);
        }

        return destination;
    }

    /// <summary>Finalizes one persisted tree after its last bounded payload wave has completed.</summary>
    /// <param name="plan">The plan whose generated identities were captured after every payload save.</param>
    /// <param name="destination">The tree interval reserved before the first payload wave.</param>
    /// <param name="placement">The insertion placement, or null for a new tree.</param>
    /// <param name="cancellationToken">The token used for final parent, ordering and scalar refresh work.</param>
    internal async Task FinalizePreparedAsync(
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> plan,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination,
        NestedSetPlacement? placement,
        CancellationToken cancellationToken
    )
    {
        var persistence = new NestedSetBulkStore<TEntity, TKey, TTreeId, TScope>(_store, plan);

        if (_store.Map.Order is not null)
        {
            var ranks = await persistence
                .ReadRanksAsync(destination.Boundary, cancellationToken)
                .ConfigureAwait(false);

            plan.PrepareGeometry(destination, ranks, cancellationToken);
        }

        await persistence
            .FinalizeAsync(destination.Parent, cancellationToken)
            .ConfigureAwait(false);

        if (_store.Map.Order is not null)
        {
            await ReorderDestinationAsync(plan, destination.Parent, cancellationToken)
                .ConfigureAwait(false);
        }

        if (placement is not null)
        {
            await NestedSetTreeRegistryState
                .TouchAsync(_store.Context, _store.LockRequest(NestedSetTreeLockMode.Existing), cancellationToken)
                .ConfigureAwait(false);
        }

        // WHY: Every insertion batch is detached before finalization. Refresh therefore cannot populate mapped
        // navigation graphs and its working set remains bounded independently of total import size.
        await persistence
            .RefreshAsync(destination.Boundary, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Places imported roots using native order while preserving optional existing manual placements.
    /// </summary>
    private async Task ReorderDestinationAsync(
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> plan,
        NestedSetParent<TKey> parent,
        CancellationToken cancellationToken
    )
    {
        var order = _store.Map.Order!;
        var canonical = await order
            .Apply(_store.Siblings(parent))
            .Select(_store.Map.Projection)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (order.Mode == NestedSetOrderMode.Strict)
        {
            await new NestedSetSiblingReorderer<TEntity, TKey, TTreeId, TScope>(_store)
                .ReorderAsync(canonical, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        var imported = new HashSet<TKey>(plan.Roots.Select(index => plan.Nodes[index].Key), _store.Map.KeyComparer);

        var physical = new LinkedList<NestedSetNode<TKey>>(
            canonical
                .Where(node => !imported.Contains(node.Key))
                .OrderBy(node => node.Left));

        var links = new Dictionary<TKey, LinkedListNode<NestedSetNode<TKey>>>(_store.Map.KeyComparer);

        for (var link = physical.First; link is not null; link = link.Next)
        {
            links.Add(link.Value.Key, link);
        }

        LinkedListNode<NestedSetNode<TKey>>? predecessor = null;

        foreach (var node in canonical)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (imported.Contains(node.Key))
            {
                // WHY: Native predecessor placement matches flexible single insertion. Existing sibling nodes
                // stay linked in their current relative order even when that order deliberately overrides the rule.
                predecessor = predecessor is null ? physical.AddFirst(node) : physical.AddAfter(predecessor, node);
                links.Add(node.Key, predecessor);
            }
            else
            {
                predecessor = links[node.Key];
            }
        }

        await new NestedSetSiblingReorderer<TEntity, TKey, TTreeId, TScope>(_store)
            .ReorderAsync(physical.ToArray(), cancellationToken)
            .ConfigureAwait(false);
    }
}
