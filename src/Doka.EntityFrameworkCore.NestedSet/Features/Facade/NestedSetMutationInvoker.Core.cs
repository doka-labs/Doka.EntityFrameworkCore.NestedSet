namespace Doka.EntityFrameworkCore.NestedSet.Features.Facade;

/// <summary>Dispatches feature operations directly over exact mapped node, scope, and tree types.</summary>
/// <typeparam name="TEntity">The configured ordinary or named shared hierarchy entity.</typeparam>
/// <typeparam name="TKey">The exact mapped node key type.</typeparam>
/// <typeparam name="TTreeId">The exact mapped tree identity type.</typeparam>
/// <typeparam name="TScope">The mapped scope type, or the internal scopeless marker.</typeparam>
internal sealed partial class
    NestedSetMutationInvoker<TEntity, TKey, TTreeId, TScope> : NestedSetMutationInvoker<TEntity>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    // WHY: Binding validates requested scalar types against model metadata before dispatch. Generic overrides
    // select stateless typed cores directly, preserving values without casts, adapters, or extra mutable state.

    /// <summary>Creates one exact-tree structural store for identity and root queries.</summary>
    private static NestedSetStore<TEntity, TKey, TTreeId, TScope> Store(
        NestedSetMutationBinding<TEntity> binding,
        TTreeId treeId
    ) => new(binding.Context, binding.EntityType, Scope(binding), treeId);

    /// <summary>Returns the mapped Scope or the zero-sized scopeless marker.</summary>
    private static TScope Scope(
        NestedSetMutationBinding<TEntity> binding
    ) => binding.GetScope<TScope>();

    /// <summary>Captures a mapped key before caller-owned mutable values can change across an await.</summary>
    private static TKey SnapshotKey(
        NestedSetMutationBinding<TEntity> binding,
        TKey key
    )
    {
        // WHY: Identity discovery and locked execution must refer to the same requested node. A mutable
        // key can otherwise redirect a later write to another node even when both belong to the same tree.

        var snapshot = NestedSetTypedValue<TKey>.Snapshot(binding.Descriptor.NodeKey.Resolve(binding.EntityType), key);

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return snapshot;
    }

    /// <summary>Rejects known invalid caller state before an anchored mutation resolves its persisted tree.</summary>
    private static void RequireNoPendingWrites(
        NestedSetMutationBinding<TEntity> binding,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: Reject registered writes before lookup without a global tracker scan. Full detection belongs
        // after resolution, where the executor also catches CLR edits introduced by query interceptors.
        NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>.RequireNoPendingWrites(binding.Context);
    }

    /// <summary>Resolves one anchor's persisted TreeId without materializing its entity payload.</summary>
    private static async Task<TTreeId> ResolveTreeIdAsync(
        NestedSetMutationBinding<TEntity> binding,
        TKey key,
        CancellationToken cancellationToken
    )
    {
        var values = await ScopedNodes(binding)
            .Where(NestedSetKeyFilter<TEntity>.Equal(binding.Descriptor.NodeKey.Name, key))
            .OrderBy(node => EF.Property<TKey>(node, binding.Descriptor.NodeKey.Name))
            .Select(node => EF.Property<TTreeId>(node, binding.Descriptor.TreeId.Name))
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return values.Count switch
        {
            1 => values[0],
            0 => throw new NestedSetException(
                NestedSetErrorCode.NodeNotFound,
                "The node does not exist in the selected Scope."),
            _ => throw new NestedSetException(
                NestedSetErrorCode.InvalidStructure,
                "The node key resolves to more than one hierarchy row."),
        };
    }

    /// <summary>Resolves source and destination TreeIds in one database command.</summary>
    private static async Task<(TTreeId Source, TTreeId Target)> ResolveTreeIdsAsync(
        NestedSetMutationBinding<TEntity> binding,
        TKey source,
        TKey target,
        CancellationToken cancellationToken
    )
    {
        var rows = await NestedSetTrackedRowset<TEntity>
            .Match(ScopedNodes(binding), binding.Descriptor.NodeKey.Name, new[] { source, target })
            .Select(row => new TreeIdentity
            {
                Ordinal = row.Ordinal,
                TreeId = EF.Property<TTreeId>(row.Entity, binding.Descriptor.TreeId.Name),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sourceRows = rows
            .Where(row => row.Ordinal == 0)
            .ToArray();

        var targetRows = rows
            .Where(row => row.Ordinal == 1)
            .ToArray();

        if (sourceRows.Length != 1
            || targetRows.Length != 1)
        {
            throw new NestedSetException(
                sourceRows.Length == 0 || targetRows.Length == 0
                    ? NestedSetErrorCode.NodeNotFound
                    : NestedSetErrorCode.InvalidStructure,
                "Source and destination must each resolve to exactly one hierarchy row in the selected Scope.");
        }

        return (sourceRows[0].TreeId, targetRows[0].TreeId);
    }

    /// <summary>Compares one materialized identity to an anchor under native database equality.</summary>
    private static Task<bool> BelongsToTreeAsync(
        NestedSetMutationBinding<TEntity> binding,
        TKey key,
        TTreeId treeId,
        CancellationToken cancellationToken
    ) => ScopedNodes(binding)
        .Where(NestedSetKeyFilter<TEntity>.Equal(binding.Descriptor.NodeKey.Name, key))
        .AnyAsync(
            node => EF
                .Property<TTreeId>(node, binding.Descriptor.TreeId.Name)
                .Equals(treeId),
            cancellationToken);

    /// <summary>Uses definite provider equality before querying native database aliases of unequal values.</summary>
    private static Task<bool> SameTreeAsync(
        NestedSetMutationBinding<TEntity> binding,
        TKey anchor,
        TTreeId sourceTreeId,
        TTreeId targetTreeId,
        CancellationToken cancellationToken
    )
    {
        if (NestedSetTypedValue<TTreeId>.Matches(
                binding.Descriptor.TreeId.Resolve(binding.EntityType),
                sourceTreeId,
                targetTreeId))
        {
            return Task.FromResult(true);
        }

        // WHY: Unequal CLR/provider values may still identify one row through database collation or padding.
        // Both paths still re-read source and destination geometry from exact stores under registry locks.
        return BelongsToTreeAsync(binding, anchor, sourceTreeId, cancellationToken);
    }

    /// <summary>Classifies a stale pre-lock tree identity after the mutation has rolled back.</summary>
    private static async Task ExecuteResolvedAsync(
        NestedSetMutationBinding<TEntity> binding,
        IReadOnlyList<(TKey Key, TTreeId TreeId)> anchors,
        Func<Task> operation,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await operation()
                .ConfigureAwait(false);
        }
        catch (NestedSetException failure) when (failure.Code is NestedSetErrorCode.NodeNotFound
                                                     or NestedSetErrorCode.TreeNotFound
                                                     or NestedSetErrorCode.TreeIdUnavailable)
        {
            // WHY: Facade anchors are resolved before the lock. A concurrent cross-tree move can make the
            // exact-tree operation report NodeNotFound or TreeIdUnavailable even while the node still exists.
            // Classification happens after transaction/savepoint cleanup; automatically replaying to write
            // could replay unrelated application work in a caller-owned transaction.
            foreach (var (key, treeId) in anchors)
            {
                if (await BelongsToTreeAsync(binding, key, treeId, cancellationToken)
                        .ConfigureAwait(false))
                {
                    continue;
                }

                var exists = await ScopedNodes(binding)
                    .Where(NestedSetKeyFilter<TEntity>.Equal(binding.Descriptor.NodeKey.Name, key))
                    .AnyAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (exists)
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.ConcurrentTreeIdentity,
                        "A hierarchy anchor changed trees during the operation.",
                        failure);
                }

                throw new NestedSetException(
                    NestedSetErrorCode.NodeNotFound,
                    "A hierarchy anchor no longer exists in the selected Scope.",
                    failure);
            }

            throw;
        }
    }

    /// <summary>Builds the internal structural root with filters bypassed and Scope reapplied.</summary>
    private static IQueryable<TEntity> ScopedNodes(
        NestedSetMutationBinding<TEntity> binding
    )
    {
        var nodes = NestedSetEntityAccess<TEntity>
            .Set(binding.Context, binding.EntityType)
            .IgnoreQueryFilters()
            .AsNoTracking();

        if (binding.Descriptor.Scope is { } scope)
        {
            nodes = nodes.Where(NestedSetKeyFilter<TEntity>.Equal(scope.Name, binding.GetScope<TScope>()));
        }

        return nodes;
    }

    /// <summary>Carries one resolved tree identity without materializing domain payload.</summary>
    private sealed class TreeIdentity
    {
        /// <summary>Gets the requested anchor ordinal in the matched rowset.</summary>
        public int Ordinal { get; init; }

        /// <summary>Gets the persisted typed tree identity.</summary>
        public TTreeId TreeId { get; init; } = default!;
    }
}
