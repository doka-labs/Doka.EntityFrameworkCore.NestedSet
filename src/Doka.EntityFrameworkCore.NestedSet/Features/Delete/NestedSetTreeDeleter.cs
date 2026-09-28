namespace Doka.EntityFrameworkCore.NestedSet.Features.Delete;

/// <summary>Deletes one exact tree without first resolving or materializing its root.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The optional mapped scope type or the scope-free marker.</typeparam>
internal static class NestedSetTreeDeleter<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Deletes all rows and tombstones the locked registry identity in one transaction.</summary>
    internal static Task DeleteAsync(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        CancellationToken cancellationToken
    )
    {
        var request = store.LockRequest(NestedSetTreeLockMode.Existing);
        var executor = new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(
            store.Context,
            store.Map.EntityType);

        return executor.ExecuteAsync(
            async token =>
            {
                var physicalDelete = new NestedSetPhysicalDelete<TEntity, TKey, TTreeId, TScope>(store);

                if (physicalDelete.IsRequired
                    || !NestedSetProviderCapabilities.Resolve(store.Context).SupportsStatementAtomicSelfReferentialDelete)
                {
                    // WHY: Doka and SQLite must tolerate restrictive self-FKs created outside the EF model.
                    await store
                        .SetParentAsync(store.Nodes.Where(store.HasParent()), default, token)
                        .ConfigureAwait(false);
                }

                var affected = physicalDelete.IsRequired
                    ? await physicalDelete
                        .ExecuteAsync(store.Nodes, token)
                        .ConfigureAwait(false)
                    : await NestedSetTelemetry
                        .TrackRows(store.Nodes.ExecuteDeleteAsync(token))
                        .ConfigureAwait(false);

                if (affected == 0)
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidStructure,
                        "The active tree registry row has no hierarchy root.");
                }

                await NestedSetTreeRegistryState
                    .TombstoneAsync(store.Context, request, token)
                    .ConfigureAwait(false);
            },
            [request],
            cancellationToken);
    }
}
