namespace Doka.EntityFrameworkCore.NestedSet.Features.Facade;

internal sealed partial class
    NestedSetMutationInvoker<TEntity, TKey, TTreeId, TScope> : NestedSetMutationInvoker<TEntity>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <inheritdoc />
    internal override Task InsertSubtreeAsync<TRequestedKey>(
        NestedSetMutationBinding<TEntity> binding,
        NestedSetBranch<TEntity> subtree,
        TRequestedKey parent,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TRequestedKey, TTreeId, TScope>.InsertSubtreeCoreAsync(
        binding,
        subtree,
        parent,
        cancellationToken);

    /// <summary>Imports a detached branch using a typed snapshotted parent key and exact-tree store.</summary>
    private static Task InsertSubtreeCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        NestedSetBranch<TEntity> subtree,
        TKey parent,
        CancellationToken cancellationToken
    ) => NestedSetTelemetry.ExecuteAsync(
        binding.Context,
        "insert_subtree",
        async token =>
        {
            RequireNoPendingWrites(binding, token);
            var parentKey = SnapshotKey(binding, parent);
            var treeId = await ResolveTreeIdAsync(binding, parentKey, token)
                .ConfigureAwait(false);
            var store = Store(binding, treeId);

            var insert = new NestedSetBulkInsert<TEntity, TKey, TTreeId, TScope>(
                store,
                new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(binding.Context, binding.EntityType),
                new NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>(store));

            await ExecuteResolvedAsync(
                    binding,
                    [(parentKey, treeId)],
                    () => insert.InsertSubtreeAsync(subtree, parentKey, token),
                    token)
                .ConfigureAwait(false);
        },
        cancellationToken);

    /// <inheritdoc />
    internal override Task InsertForestAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        IReadOnlyList<NestedSetTreeImport<TEntity, TRequestedTreeId>> trees,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TKey, TRequestedTreeId, TScope>.InsertForestCoreAsync(
        binding,
        trees,
        cancellationToken);

    /// <summary>Preserves the caller's typed forest list without request-array conversion.</summary>
    private static Task InsertForestCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        IReadOnlyList<NestedSetTreeImport<TEntity, TTreeId>> trees,
        CancellationToken cancellationToken
    ) => NestedSetTelemetry.ExecuteAsync(
        binding.Context,
        "insert_forest",
        token => new NestedSetForestInsert<TEntity, TKey, TTreeId, TScope>(
            binding.Context,
            binding.EntityType,
            Scope(binding)).InsertAsync(trees, token),
        cancellationToken);
}
