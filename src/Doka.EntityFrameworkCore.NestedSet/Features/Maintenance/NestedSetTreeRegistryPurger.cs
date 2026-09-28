namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

/// <summary>Removes one retired registry identity under the normal tree transaction protocol.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TScope">The optional mapped scope type or the scope-free marker.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
internal static class NestedSetTreeRegistryPurger<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Purges an empty tombstone after locking and verifying its complete tree identity.</summary>
    internal static Task PurgeAsync(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        CancellationToken cancellationToken
    )
    {
        var request = store.LockRequest(NestedSetTreeLockMode.Tombstoned);
        var executor = new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(
            store.Context,
            store.Map.EntityType);

        return executor.ExecuteAsync(
            async token =>
            {
                if (await store
                        .Nodes
                        .AnyAsync(token)
                        .ConfigureAwait(false))
                {
                    // WHY: A tombstone with live nodes proves out-of-protocol database damage. Removing its
                    // reservation would permit two logical generations to share one TreeId.
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidStructure,
                        "The tombstoned TreeId still has hierarchy nodes and cannot be purged.");
                }

                await NestedSetTreeRegistryState
                    .PurgeAsync(store.Context, request, token)
                    .ConfigureAwait(false);
            },
            [request],
            cancellationToken);
    }
}
