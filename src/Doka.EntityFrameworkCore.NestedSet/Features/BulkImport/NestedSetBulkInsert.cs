namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

/// <summary>Imports one root branch or subtree using an exact-tree interval and bounded insertion saves.</summary>
/// <typeparam name="TEntity">The detached hierarchy entity type.</typeparam>
/// <typeparam name="TKey">The mapped primary key type.</typeparam>
/// <typeparam name="TTreeId">The mapped tree identity type.</typeparam>
/// <typeparam name="TScope">The mapped scope type.</typeparam>
internal sealed partial class NestedSetBulkInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> _executor;
    private readonly NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope> _placement;
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;

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
        var inputs = NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>.CreateImportInputs([plan]);
        callbackWrites.CaptureInputOwners(_store.Context, inputs);
        using var inputScope = NestedSetSaveChanges.EnterImportInputs(_store.Context, inputs);

        try
        {
            await _executor
                .ExecuteAsync(
                    operationCancellationToken => InsertPreparedAsync(
                        plan,
                        parent,
                        callbackWrites,
                        operationCancellationToken),
                    [_store.LockRequest(NestedSetTreeLockMode.Existing)],
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception operationError)
        {
            List<Exception>? restoreErrors = null;

            try
            {
                inputs.DetachUnexpected(_store.Context, plan.ActiveIdentities.Select(identity => identity.UpdateEntry));
            }
            catch (Exception restoreError)
            {
                (restoreErrors ??= []).Add(restoreError);
            }

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
                plan.StageBatch(destination, offset, count, callbackWrites, cancellationToken);
                IReadOnlySet<IUpdateEntry> introducedEntries;
                IReadOnlySet<object> introducedEntities;

                using (var managedSave = NestedSetSaveChanges.EnterManagedSave(
                    _store.Context,
                    plan.ManagedEntities,
                    plan.RequireSavedStage,
                    plan.RefreshInsertionIdentities))
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

                    introducedEntries = managedSave.PersistedEntries;
                    introducedEntities = managedSave.PersistedEntities;
                }

                plan.RequireSavedStage();
                NestedSetTelemetry.RecordRowsAffected(count);
                NestedSetTelemetry.RecordBatch();
                callbackWrites.RecordSavedRelationships(plan.ActiveIdentities, introducedEntries, introducedEntities);
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
}
