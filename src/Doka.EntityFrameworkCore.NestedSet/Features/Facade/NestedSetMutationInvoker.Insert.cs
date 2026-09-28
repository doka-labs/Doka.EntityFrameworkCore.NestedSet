namespace Doka.EntityFrameworkCore.NestedSet.Features.Facade;

internal sealed partial class
    NestedSetMutationInvoker<TEntity, TKey, TTreeId, TScope> : NestedSetMutationInvoker<TEntity>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <inheritdoc />
    internal override Task InsertRootAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TEntity entity,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TKey, TRequestedTreeId, TScope>.InsertRootCoreAsync(
        binding,
        entity,
        treeId,
        cancellationToken);

    /// <summary>Creates a root using the validated TreeId without erasing its scalar type.</summary>
    private static Task InsertRootCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TEntity entity,
        TTreeId treeId,
        CancellationToken cancellationToken
    )
    {
        var store = Store(binding, treeId);
        var insert = Insert(binding, store);

        return NestedSetTelemetry.ExecuteAsync(
            binding.Context,
            "insert_root",
            token => insert.InsertRootAsync(entity, token),
            cancellationToken);
    }

    /// <inheritdoc />
    internal override Task InsertChildAsync<TRequestedKey>(
        NestedSetMutationBinding<TEntity> binding,
        TEntity entity,
        TRequestedKey parent,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TRequestedKey, TTreeId, TScope>.InsertChildCoreAsync(
        binding,
        entity,
        parent,
        placement,
        automatic,
        cancellationToken);

    /// <summary>Resolves and inserts relative to a parent or sibling while retaining its mapped key type.</summary>
    private static Task InsertChildCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TEntity entity,
        TKey parent,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    ) => NestedSetTelemetry.ExecuteAsync(
        binding.Context,
        InsertOperationName(placement, automatic),
        async token =>
        {
            RequireNoPendingWrites(binding, token);
            var key = SnapshotKey(binding, parent);
            var treeId = await ResolveTreeIdAsync(binding, key, token).ConfigureAwait(false);
            var insert = Insert(binding, Store(binding, treeId));

            await ExecuteResolvedAsync(
                    binding,
                    [(key, treeId)],
                    () => InsertRelativeAsync(insert, entity, key, placement, automatic, token),
                    token)
                .ConfigureAwait(false);
        },
        cancellationToken);

    /// <summary>Creates only the insert feature and its required exact-tree collaborators.</summary>
    private static NestedSetInsert<TEntity, TKey, TTreeId, TScope> Insert(
        NestedSetMutationBinding<TEntity> binding,
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store
    ) => new(
        store,
        new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(binding.Context, binding.EntityType),
        new NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>(store));

    /// <summary>Selects the stable telemetry name without constructing a mutation task.</summary>
    private static string InsertOperationName(
        NestedSetPlacement placement,
        bool automatic
    )
    {
        if (automatic)
        {
            return "insert_child";
        }

        return placement switch
        {
            NestedSetPlacement.FirstChild or NestedSetPlacement.LastChild => "insert_child",
            NestedSetPlacement.Before => "insert_before",
            NestedSetPlacement.After => "insert_after",
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };
    }

    /// <summary>Selects automatic ordering or the explicitly requested sibling placement.</summary>
    private static Task InsertRelativeAsync(
        NestedSetInsert<TEntity, TKey, TTreeId, TScope> insert,
        TEntity entity,
        TKey anchor,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    )
    {
        if (automatic)
        {
            return insert.InsertChildAsync(entity, anchor, cancellationToken);
        }

        return placement switch
        {
            NestedSetPlacement.FirstChild => insert.InsertAsFirstChildAsync(entity, anchor, cancellationToken),
            NestedSetPlacement.LastChild => insert.InsertAsLastChildAsync(entity, anchor, cancellationToken),
            NestedSetPlacement.Before => insert.InsertBeforeAsync(entity, anchor, cancellationToken),
            NestedSetPlacement.After => insert.InsertAfterAsync(entity, anchor, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };
    }
}
