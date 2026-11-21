namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

/// <summary>Imports independently identified root branches under one ordered lock and transaction boundary.</summary>
/// <typeparam name="TEntity">The configured hierarchy entity.</typeparam>
/// <typeparam name="TKey">The mapped NodeKey type.</typeparam>
/// <typeparam name="TScope">The mapped Scope type or the scopeless marker.</typeparam>
/// <typeparam name="TTreeId">The mapped TreeId type.</typeparam>
internal sealed partial class NestedSetForestInsert<TEntity, TKey, TTreeId, TScope>
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

        // WHY: Keep prepared tree plans and registry lock requests aligned with input trees.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
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
        var inputs = NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
            .CreateImportInputs(prepared.Select(tree => tree.Plan));

        callbackWrites.CaptureInputOwners(_context, inputs);
        using var inputScope = NestedSetSaveChanges.EnterImportInputs(_context, inputs);

        try
        {
            await executor
                .ExecuteAsync(
                    async operationCancellationToken =>
                    {
                        // WHY: TreeId equality belongs to the database collation and converter, not CLR equality.
                        // All registry rows are already reserved, so this check precedes the first hierarchy write.
                        await NestedSetTreeLocks
                            .RequireDistinctAsync(_context, lockRequests, operationCancellationToken)
                            .ConfigureAwait(false);

                        await InsertPreparedAsync(prepared, knownTracked, callbackWrites, operationCancellationToken)
                            .ConfigureAwait(false);
                    },
                    lockRequests,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception operationError)
        {
            Exception? cleanupError = null;

            try
            {
                inputs.DetachUnexpected(
                    _context,
                    prepared
                        .SelectMany(tree => tree.Plan.ActiveIdentities)
                        .Select(identity => identity.UpdateEntry));
            }
            catch (Exception error)
            {
                cleanupError = error;
            }

            Restore(
                prepared,
                callbackWrites,
                cleanupError is null
                    ? operationError
                    : new AggregateException(
                        "Import input cleanup failed. Discard the context.",
                        operationError,
                        cleanupError));

            if (cleanupError is not null)
            {
                throw new AggregateException(
                    "Import input cleanup failed. Discard the context.",
                    operationError,
                    cleanupError);
            }

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
            var nodeItems = tree.Plan.Nodes;

            // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (int nodeIndex = 0, nodeCount = nodeItems.Count; nodeIndex < nodeCount; nodeIndex++)
            {
                var node = nodeItems[nodeIndex];

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
}
