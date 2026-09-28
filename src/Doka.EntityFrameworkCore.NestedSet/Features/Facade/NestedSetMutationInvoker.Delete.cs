namespace Doka.EntityFrameworkCore.NestedSet.Features.Facade;

internal sealed partial class
    NestedSetMutationInvoker<TEntity, TKey, TTreeId, TScope> : NestedSetMutationInvoker<TEntity>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <inheritdoc />
    internal override Task DeleteAsync<TRequestedKey>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedKey key,
        bool subtree,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TRequestedKey, TTreeId, TScope>.DeleteCoreAsync(
        binding,
        key,
        subtree,
        cancellationToken);

    /// <summary>Deletes a node or branch selected by a typed key snapshotted before identity resolution.</summary>
    private static Task DeleteCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TKey key,
        bool subtree,
        CancellationToken cancellationToken
    ) => NestedSetTelemetry.ExecuteAsync(
        binding.Context,
        subtree ? "delete_subtree" : "delete",
        async token =>
        {
            RequireNoPendingWrites(binding, token);
            var nodeKey = SnapshotKey(binding, key);
            var treeId = await ResolveTreeIdAsync(binding, nodeKey, token)
                .ConfigureAwait(false);
            var store = Store(binding, treeId);

            var delete = new NestedSetDelete<TEntity, TKey, TTreeId, TScope>(
                store,
                new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(binding.Context, binding.EntityType));

            await ExecuteResolvedAsync(
                    binding,
                    [(nodeKey, treeId)],
                    () => subtree ? delete.DeleteSubtreeAsync(nodeKey, token) : delete.DeleteAsync(nodeKey, token),
                    token)
                .ConfigureAwait(false);
        },
        cancellationToken);

    /// <inheritdoc />
    internal override Task DeleteTreeAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TKey, TRequestedTreeId, TScope>.DeleteTreeCoreAsync(
        binding,
        treeId,
        cancellationToken);

    /// <summary>Deletes exactly one typed tree and retains its lifecycle tombstone.</summary>
    private static Task DeleteTreeCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TTreeId treeId,
        CancellationToken cancellationToken
    )
    {
        var store = Store(binding, treeId);

        return NestedSetTelemetry.ExecuteAsync(
            binding.Context,
            "delete_tree",
            token => NestedSetTreeDeleter<TEntity, TKey, TTreeId, TScope>.DeleteAsync(store, token),
            cancellationToken);
    }

    /// <inheritdoc />
    internal override Task PurgeTreeIdAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TKey, TRequestedTreeId, TScope>.PurgeTreeIdCoreAsync(
        binding,
        treeId,
        cancellationToken);

    /// <summary>Purges a lifecycle tombstone selected by its exact typed tree identity.</summary>
    private static Task PurgeTreeIdCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TTreeId treeId,
        CancellationToken cancellationToken
    )
    {
        var store = Store(binding, treeId);

        return NestedSetTelemetry.ExecuteAsync(
            binding.Context,
            "purge_tree_id",
            token => NestedSetTreeRegistryPurger<TEntity, TKey, TTreeId, TScope>.PurgeAsync(store, token),
            cancellationToken);
    }
}
