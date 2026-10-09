namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

/// <summary>Imports independently identified root branches under one ordered lock and transaction boundary.</summary>
/// <typeparam name="TEntity">The configured hierarchy entity.</typeparam>
/// <typeparam name="TKey">The mapped NodeKey type.</typeparam>
/// <typeparam name="TScope">The mapped Scope type or the scopeless marker.</typeparam>
/// <typeparam name="TTreeId">The mapped TreeId type.</typeparam>
internal sealed class NestedSetForestInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly DbContext _context;
    private readonly IEntityType _entityType;
    private readonly TScope _scope;

    /// <summary>Captures the caller-owned context and immutable Scope binding.</summary>
    /// <param name="context">The caller-owned context used only for this forest operation.</param>
    /// <param name="entityType">The exact mapped hierarchy type selected by the public facade.</param>
    /// <param name="scope">The configured scope value or the scopeless marker.</param>
    internal NestedSetForestInsert(
        DbContext context,
        IEntityType entityType,
        TScope scope
    )
    {
        _context = context;
        _entityType = entityType;
        _scope = scope;
    }

    /// <summary>Validates every root before atomically creating all requested trees.</summary>
    /// <param name="trees">The typed independent tree identities and their detached root branches.</param>
    /// <param name="cancellationToken">The token used for input validation, lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal async Task InsertAsync(
        IReadOnlyList<NestedSetTreeImport<TEntity, TTreeId>> trees,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(trees);
        cancellationToken.ThrowIfCancellationRequested();

        if (trees.Count == 0)
        {
            return;
        }

        var mapping = NestedSetMapping<TEntity, TKey, TScope>.For(_context, _entityType);
        var executor = new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(_context, mapping.EntityType);
        var knownTracked = NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>.CaptureKnownTracked(_context, _entityType);

        var prepared = new PreparedTree[trees.Count];
        var lockRequests = new NestedSetTreeLockRequest<TTreeId, TScope>[trees.Count];
        for (var index = 0; index < trees.Count; index++)
        {
            var request = trees[index];
            ArgumentNullException.ThrowIfNull(request);

            var store = new NestedSetStore<TEntity, TKey, TTreeId, TScope>(
                _context,
                _entityType,
                _scope,
                request.TreeId);

            var insertion = new NestedSetBulkInsert<TEntity, TKey, TTreeId, TScope>(
                store,
                executor,
                new NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>(store));

            var plan = insertion.Prepare([request.Root], knownTracked, cancellationToken);

            prepared[index] = new PreparedTree(insertion, plan, default);
            lockRequests[index] = store.LockRequest(NestedSetTreeLockMode.New);
        }

        RequireDistinctInputs(prepared, mapping.KeyComparer, cancellationToken);

        // WHY: Callback writes from all trees share one persistence order; restoring them as one log keeps an
        // entry changed by several tree saves at its earliest pending database-relative state.
        var callbackWrites = new NestedSetCallbackWrites();

        try
        {
            await executor
                .ExecuteAsync(
                    async token =>
                    {
                        // WHY: TreeId equality belongs to the database collation and converter, not CLR equality.
                        // All registry rows are already reserved, so this check precedes the first hierarchy write.
                        await NestedSetTreeLocks
                            .RequireDistinctAsync(_context, lockRequests, token)
                            .ConfigureAwait(false);

                        await InsertPreparedAsync(prepared, knownTracked, callbackWrites, token)
                            .ConfigureAwait(false);
                    },
                    lockRequests,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception operationError)
        {
            Restore(prepared, callbackWrites, operationError);
            throw;
        }

        Detach(prepared);
    }

    /// <summary>Checks cross-tree uniqueness without repeating a single plan's complete identity validation.</summary>
    /// <param name="prepared">The independently validated tree plans.</param>
    /// <param name="keyComparer">The hierarchy's exact mapped primary-key comparer.</param>
    /// <param name="cancellationToken">The token checked throughout potentially large forest validation.</param>
    private static void RequireDistinctInputs(
        PreparedTree[] prepared,
        IEqualityComparer<TKey> keyComparer,
        CancellationToken cancellationToken
    )
    {
        // WHY: Each plan already rejects duplicate references and assigned keys. A single tree has no
        // cross-plan invariant; duplicating its indexes increases peak heap and allocation without protection.
        if (prepared.Length == 1)
        {
            return;
        }

        var entities = new HashSet<TEntity>(ReferenceEqualityComparer.Instance);
        var assignedKeys = new HashSet<TKey>(keyComparer);

        foreach (var tree in prepared)
        {
            foreach (var node in tree.Plan.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!entities.Add(node.Entity))
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidImport,
                        "Each imported entity must occur exactly once in the forest.");
                }

                if (node.HasAssignedKey
                    && !assignedKeys.Add(node.Key))
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidImport,
                        "Imported entities must have distinct assigned primary keys.");
                }
            }
        }
    }

    /// <summary>Shares bounded payload waves across trees while preserving independent structural identities.</summary>
    /// <param name="prepared">The validated tree plans whose complete registry lock set is already held.</param>
    /// <param name="knownTracked">The immutable hierarchy identity baseline captured before import.</param>
    /// <param name="callbackWrites">The shared rollback log for every accepted application callback write.</param>
    /// <param name="cancellationToken">The token used for staging, persistence and structural finalization.</param>
    private async Task InsertPreparedAsync(
        PreparedTree[] prepared,
        HashSet<TEntity> knownTracked,
        NestedSetCallbackWrites callbackWrites,
        CancellationToken cancellationToken
    )
    {
        var order = NestedSetMapping<TEntity, TKey, TScope>.For(_context, _entityType).Order;

        for (var index = 0; index < prepared.Length; index++)
        {
            var tree = prepared[index];
            var destination = await tree
                .Insertion
                .PrepareDestinationAsync(tree.Plan, default, null, cancellationToken)
                .ConfigureAwait(false);

            prepared[index] = tree with { Destination = destination };

            if (order is null)
            {
                tree.Plan.PrepareGeometry(destination, null, cancellationToken);
            }
        }

        var active = new List<PreparedTree>(Math.Min(prepared.Length, NestedSetBatch.MaximumRows));
        var expected = new HashSet<TEntity>(ReferenceEqualityComparer.Instance);
        var treeIndex = 0;
        var treeOffset = 0;
        var autoSavepoints = _context.Database.AutoSavepointsEnabled;

        try
        {
            // WHY: Every wave shares the executor's rollback boundary. Per-save EF savepoints would add commands
            // while all tree locks remain held, without making a partial forest independently recoverable.
            _context.Database.AutoSavepointsEnabled = false;

            while (treeIndex < prepared.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                active.Clear();
                expected.Clear();
                var remaining = NestedSetBatch.MaximumRows;

                while (remaining > 0
                       && treeIndex < prepared.Length)
                {
                    var tree = prepared[treeIndex];
                    var count = Math.Min(remaining, tree.Plan.Nodes.Count - treeOffset);
                    tree.Plan.StageBatch(tree.Destination, treeOffset, count, cancellationToken);
                    active.Add(tree);

                    for (var index = treeOffset; index < treeOffset + count; index++)
                    {
                        expected.Add(tree.Plan.Nodes[index].Entity);
                    }

                    treeOffset += count;
                    remaining -= count;

                    if (treeOffset == tree.Plan.Nodes.Count)
                    {
                        treeIndex++;
                        treeOffset = 0;
                    }
                }

                // WHY: Independent one-node trees must share payload saves too. The wave guard authorizes the
                // exact union of staged roots while each plan still verifies its own typed tree coordinates.
                using (var managedSave = NestedSetSaveChanges.EnterManagedSave(
                           _context,
                           active.SelectMany(tree => tree.Plan.ManagedEntities),
                           () => RequireSavedWave(active, knownTracked, expected),
                           () =>
                           {
                               foreach (var tree in active)
                               {
                                   tree.Plan.RefreshInsertionIdentities();
                               }
                           }))
                {
                    try
                    {
                        await _context
                            .SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        callbackWrites.Add(managedSave.CallbackWrites);
                    }

                    managedSave.RequirePersisted();
                }

                RequireSavedWave(active, knownTracked, expected);
                NestedSetTelemetry.RecordRowsAffected(NestedSetBatch.MaximumRows - remaining);
                NestedSetTelemetry.RecordBatch();

                foreach (var tree in active)
                {
                    tree.Plan.CompleteBatch();
                }
            }
        }
        finally
        {
            _context.Database.AutoSavepointsEnabled = autoSavepoints;
        }

        foreach (var tree in prepared)
        {
            await tree
                .Insertion
                .FinalizePreparedAsync(tree.Plan, tree.Destination, null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Validates wave membership once and then verifies every participating plan's scalar structure.</summary>
    private void RequireSavedWave(
        IReadOnlyList<PreparedTree> active,
        HashSet<TEntity> knownTracked,
        HashSet<TEntity> expected
    )
    {
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>.RequireSavedMembership(
            _context,
            _entityType,
            knownTracked,
            expected);

        foreach (var tree in active)
        {
            tree.Plan.RequireSavedGeometry();
        }
    }

    /// <summary>Restores every input after rollback and preserves both operation and cleanup failures.</summary>
    private static void Restore(
        IReadOnlyList<PreparedTree> prepared,
        NestedSetCallbackWrites callbackWrites,
        Exception operationError
    )
    {
        List<Exception>? errors = null;

        foreach (var tree in prepared)
        {
            try
            {
                tree.Plan.Restore();
            }
            catch (Exception restoreError)
            {
                (errors ??= []).Add(restoreError);
            }
        }

        try
        {
            callbackWrites.Restore();
        }
        catch (Exception restoreError)
        {
            (errors ??= []).Add(restoreError);
        }

        if (errors is not null)
        {
            throw new AggregateException(
                "Nested-set forest restoration failed. Discard the context.",
                [operationError, .. errors]);
        }
    }

    /// <summary>Ensures every successfully imported input remains detached from the caller's context.</summary>
    private static void Detach(
        IReadOnlyList<PreparedTree> prepared
    )
    {
        List<Exception>? errors = null;

        foreach (var tree in prepared)
        {
            try
            {
                tree.Plan.Detach();
            }
            catch (Exception detachError)
            {
                (errors ??= []).Add(detachError);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more imported trees could not be detached.", errors);
        }
    }

    /// <summary>Keeps one validated plan with the exact-tree insertion engine that owns it.</summary>
    private readonly record struct PreparedTree(
        NestedSetBulkInsert<TEntity, TKey, TTreeId, TScope> Insertion,
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> Plan,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination Destination
    );
}
