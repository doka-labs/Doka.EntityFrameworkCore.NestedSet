namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Executes structural mutations under a database write lock and a rollback boundary.</summary>
/// <typeparam name="TEntity">The hierarchy entity whose tracked instances become stale after bulk updates.</typeparam>
/// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
/// <typeparam name="TTreeId">The exact mapped TreeId type.</typeparam>
/// <typeparam name="TScope">The exact Scope type, or the internal scopeless marker.</typeparam>
/// <remarks>
///     The executor shares its caller-owned context and is not thread-safe. An existing EF transaction remains owned
///     by the caller; a failed savepoint rollback requires the caller to discard that transaction and context.
/// </remarks>
internal sealed class NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>The caller-owned context used for mutations and transaction discovery.</summary>
    private readonly DbContext _context;

    /// <summary>The exact typed hierarchy metadata retained without erasing known identity types.</summary>
    private readonly NestedSetMapping<TEntity, TKey, TScope> _map;

    /// <summary>The shared registry mapping identifying this hierarchy, including inherited aliases.</summary>
    private readonly NestedSetTreeRegistryMapping _registry;

    /// <summary>Connects transaction coordination to one exact hierarchy mapping.</summary>
    /// <param name="context">The caller-owned context.</param>
    /// <param name="entityType">The resolved ordinary or named shared hierarchy mapping.</param>
    internal NestedSetMutationExecutor(
        DbContext context,
        IEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entityType);

        if (!ReferenceEquals(entityType.Model, context.Model)
            || entityType.ClrType != typeof(TEntity))
        {
            throw new ArgumentException(
                "The exact entity mapping must match the context model and entity type.",
                nameof(entityType));
        }

        _map = NestedSetMapping<TEntity, TKey, TScope>.For(context, entityType);

        if (_map.TreeIdProperty.ClrType != typeof(TTreeId))
        {
            throw new InvalidOperationException("The nested-set TreeId must match TTreeId.");
        }

        _context = context;
        _registry = NestedSetTreeRegistryMapping.For(entityType);
    }

    /// <summary>Runs consistent structural reads under explicitly selected registry locks.</summary>
    /// <param name="operation">The read-only work performed while every requested lock is held.</param>
    /// <param name="requests">The exact existing tree identities stabilizing these reads.</param>
    /// <param name="cancellationToken">The token used for forward progress, but never failure rollback.</param>
    /// <returns>The complete owned transaction or caller savepoint operation.</returns>
    /// <remarks>No tracked entries are saved, rejected or refreshed by this read-only boundary.</remarks>
    internal Task ExecuteReadAsync(
        Func<CancellationToken, Task> operation,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requests);
        cancellationToken.ThrowIfCancellationRequested();
        RequireMatchingHierarchy(requests);

        foreach (var request in requests)
        {
            if (request.Mode != NestedSetTreeLockMode.Existing)
            {
                throw new ArgumentException("Consistent reads require existing tree locks.", nameof(requests));
            }
        }

        // WHY: Validation under weaker caller isolation must stabilize structure without flushing or changing
        // application-owned tracker state. Mutation guards belong exclusively to the write boundary.
        return ExecuteLockedAsync(operation, requests, cancellationToken);
    }

    /// <summary>Runs one mutation while locking only its exact tree identities.</summary>
    /// <param name="operation">The structural reads and writes performed after every registry lock is held.</param>
    /// <param name="requests">The complete existing or newly reserved tree identities.</param>
    /// <param name="cancellationToken">The token used for forward progress, but never for failure rollback.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release succeeds.</returns>
    internal Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requests);
        cancellationToken.ThrowIfCancellationRequested();
        RequireMatchingHierarchy(requests);
        RequireMutationContext(_context);
        var guard = NestedSetTrackedIdentityGuard<TEntity, TKey, TTreeId, TScope>.Capture(_context, _map, requests);

        // WHY: Coordinated saves already own tracker protection. Every other boundary observes hierarchy
        // attachments during awaited registry locks, even when no entries currently need native equality reads.
        return guard is null
            ? ExecuteLockedAsync(operation, requests, cancellationToken)
            : ExecuteGuardedAsync(operation, requests, guard, cancellationToken);
    }

    /// <summary>Shares exact lock acquisition and transaction ownership between write and read boundaries.</summary>
    private Task ExecuteLockedAsync(
        Func<CancellationToken, Task> operation,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests,
        CancellationToken cancellationToken
    ) => NestedSetTransaction.ExecuteAsync(
        _context,
        async token =>
        {
            await NestedSetTreeLocks
                .AcquireAsync(_context, requests, token)
                .ConfigureAwait(false);

            await operation(token)
                .ConfigureAwait(false);
        },
        cancellationToken);

    /// <summary>Rechecks uncertain persisted identities under locks before the first structural write.</summary>
    private async Task ExecuteGuardedAsync(
        Func<CancellationToken, Task> operation,
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests,
        NestedSetTrackedIdentityGuard<TEntity, TKey, TTreeId, TScope> guard,
        CancellationToken cancellationToken
    )
    {
        using (guard)
        {
            await ExecuteLockedAsync(
                    async token =>
                    {
                        if (guard.HasCandidates)
                        {
                            await guard
                                .RequireUnaffectedAsync(token)
                                .ConfigureAwait(false);

                            // WHY: Native membership reads add an application callback boundary. Detect payload edits
                            // once afterward; an empty hierarchy tracker adds no native reads or global detection pass.
                            RequireMutationContext(_context);
                        }
                        else
                        {
                            // WHY: Registry command callbacks may register unrelated writes even with no hierarchy
                            // entries. The state counter rejects them without rescanning a clean application tracker.
                            RequireNoPendingWrites(_context);
                        }

                        guard.VerifyUnchanged();
                        guard.Dispose();

                        await operation(token)
                            .ConfigureAwait(false);
                    },
                    requests,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Rejects foreign registry requests before acquiring any transaction or database lock.</summary>
    private void RequireMatchingHierarchy(
        IReadOnlyList<NestedSetTreeLockRequest<TTreeId, TScope>> requests
    )
    {
        foreach (var request in requests)
        {
            // WHY: Inherited hierarchy aliases share one physical registry mapping. Comparing that exact cached
            // mapping accepts their common lock domain while rejecting other entity types or context models.
            if (!ReferenceEquals(request.Mapping, _registry))
            {
                throw new ArgumentException(
                    "Every lock request must belong to this exact hierarchy.",
                    nameof(requests));
            }
        }
    }

    /// <summary>Rejects tracking state incompatible with immediate structural updates.</summary>
    /// <param name="context">The caller-owned context checked before acquiring locks or writing structure.</param>
    /// <exception cref="NestedSetException">Application writes are pending outside a coordinated save.</exception>
    internal static void RequireMutationContext(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        if (NestedSetSaveChanges.IsManagedMutation(context))
        {
            // WHY: The outer coordinated save already snapshotted the tracker, saved payload exactly once,
            // acquired every source and target tree lock, and owns rollback for this preplanned Parent move.
            return;
        }

        var tracker = context.ChangeTracker;

        // WHY: Direct CLR edits may appear during an awaited identity lookup. Detect them once immediately
        // before the mutation boundary; the earlier facade preflight checks only already registered states.
        if (tracker.AutoDetectChangesEnabled)
        {
            tracker.DetectChanges();
        }

        RequireNoPendingWrites(context);
    }

    /// <summary>Rejects registered pending writes without scanning unchanged application entities.</summary>
    /// <param name="context">The caller-owned context inspected before an anchored identity lookup.</param>
    /// <exception cref="NestedSetException">
    /// Application writes are already registered outside a coordinated save.
    /// </exception>
    /// <remarks>The mutation boundary performs full detection after any asynchronous identity lookup.</remarks>
    internal static void RequireNoPendingWrites(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        if (NestedSetSaveChanges.IsManagedMutation(context))
        {
            return;
        }

        var tracker = context.ChangeTracker;
        var automaticDetection = tracker.AutoDetectChangesEnabled;
        bool pendingChanges;

        try
        {
            // WHY: HasChanges reads EF's state counter when automatic detection is disabled. This preflight
            // must not allocate wrappers or run another global pass over unrelated clean tracked entries.
            tracker.AutoDetectChangesEnabled = false;
            pendingChanges = tracker.HasChanges();
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = automaticDetection;
        }

        // WHY: Bulk updates bypass tracking, and inserting a node must not flush unrelated pending changes.
        if (pendingChanges)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Mutations require no pending tracked changes.");
        }
    }
}
