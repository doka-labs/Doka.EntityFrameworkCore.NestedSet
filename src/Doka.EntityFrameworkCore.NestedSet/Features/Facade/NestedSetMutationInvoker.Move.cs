namespace Doka.EntityFrameworkCore.NestedSet.Features.Facade;

internal sealed partial class
    NestedSetMutationInvoker<TEntity, TKey, TTreeId, TScope> : NestedSetMutationInvoker<TEntity>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <inheritdoc />
    internal override Task MoveAsync<TRequestedKey>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedKey key,
        TRequestedKey anchor,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TRequestedKey, TTreeId, TScope>.MoveCoreAsync(
        binding,
        key,
        anchor,
        placement,
        automatic,
        cancellationToken);

    /// <summary>Moves between typed anchors while preserving pre-lock resolution and locked revalidation.</summary>
    private static Task MoveCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TKey key,
        TKey anchor,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    ) => NestedSetTelemetry.ExecuteAsync(
        binding.Context,
        MoveOperationName(placement, automatic),
        async token =>
        {
            RequireNoPendingWrites(binding, token);
            var sourceKey = SnapshotKey(binding, key);
            var anchorKey = SnapshotKey(binding, anchor);
            var (source, target) = await ResolveTreeIdsAsync(binding, sourceKey, anchorKey, token)
                .ConfigureAwait(false);

            await ExecuteResolvedAsync(
                    binding,
                    [(sourceKey, source), (anchorKey, target)],
                    async () =>
                    {
                        if (!await SameTreeAsync(binding, anchorKey, source, target, token)
                                .ConfigureAwait(false))
                        {
                            if (placement is not NestedSetPlacement.LastChild
                                || !automatic)
                            {
                                throw new NestedSetException(
                                    NestedSetErrorCode.OperationRejected,
                                    "Sibling placement requires source and destination in the same tree.");
                            }

                            await new NestedSetCrossTreeMover<TEntity, TKey, TTreeId, TScope>(
                                    binding.Context,
                                    binding.EntityType,
                                    Scope(binding))
                                .MoveToAsync(sourceKey, source, anchorKey, target, token)
                                .ConfigureAwait(false);

                            return;
                        }

                        var store = Store(binding, source);
                        var move = new NestedSetMove<TEntity, TKey, TTreeId, TScope>(
                            store,
                            new NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>(store));

                        var executor = new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(
                            binding.Context,
                            binding.EntityType);

                        await executor
                            .ExecuteAsync(
                                lockedToken => move.ExecuteAsync(
                                    sourceKey,
                                    anchorKey,
                                    placement,
                                    automatic,
                                    lockedToken),
                                [store.LockRequest(NestedSetTreeLockMode.Existing)],
                                token)
                            .ConfigureAwait(false);
                    },
                    token)
                .ConfigureAwait(false);
        },
        cancellationToken);

    /// <inheritdoc />
    internal override Task DetachAsTreeAsync<TRequestedKey, TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedKey key,
        TRequestedTreeId newTreeId,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TRequestedKey, TRequestedTreeId, TScope>.DetachAsTreeCoreAsync(
        binding,
        key,
        newTreeId,
        cancellationToken);

    /// <summary>Detaches a typed source under a target identity snapshotted before resolving the source.</summary>
    private static Task DetachAsTreeCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TKey key,
        TTreeId newTreeId,
        CancellationToken cancellationToken
    ) => NestedSetTelemetry.ExecuteAsync(
        binding.Context,
        "detach_as_tree",
        async token =>
        {
            RequireNoPendingWrites(binding, token);
            var sourceKey = SnapshotKey(binding, key);

            // WHY: Resolving the source awaits database work. Snapshot mutable target identities first so the
            // caller cannot change which registry row and destination tree the operation uses while it waits.
            var targetTreeId = NestedSetTypedValue<TTreeId>.Snapshot(
                binding.Descriptor.TreeId.Resolve(binding.EntityType),
                newTreeId);

            if (targetTreeId is null)
            {
                throw new ArgumentNullException(nameof(newTreeId));
            }

            var sourceTreeId = await ResolveTreeIdAsync(binding, sourceKey, token).ConfigureAwait(false);

            await ExecuteResolvedAsync(
                    binding,
                    [(sourceKey, sourceTreeId)],
                    () => new NestedSetCrossTreeMover<TEntity, TKey, TTreeId, TScope>(
                        binding.Context,
                        binding.EntityType,
                        Scope(binding)).DetachAsync(sourceKey, sourceTreeId, targetTreeId, token),
                    token)
                .ConfigureAwait(false);
        },
        cancellationToken);

    /// <summary>Selects the stable telemetry name for configured or explicit sibling placement.</summary>
    private static string MoveOperationName(
        NestedSetPlacement placement,
        bool automatic
    )
    {
        if (automatic)
        {
            return "move_to";
        }

        return placement switch
        {
            NestedSetPlacement.FirstChild => "move_first_child",
            NestedSetPlacement.LastChild => "move_last_child",
            NestedSetPlacement.Before => "move_before",
            NestedSetPlacement.After => "move_after",
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };
    }
}
