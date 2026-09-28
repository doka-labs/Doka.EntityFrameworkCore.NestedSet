namespace Doka.EntityFrameworkCore.NestedSet.Features.Move;

/// <summary>Moves complete subtrees between exact tree identities under deterministic registry locks.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The optional mapped scope type or the scope-free marker.</typeparam>
internal sealed class NestedSetCrossTreeMover<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly DbContext _context;
    private readonly IEntityType _entityType;
    private readonly TScope _scope;

    /// <summary>Creates a cross-tree coordinator over the caller-owned context.</summary>
    internal NestedSetCrossTreeMover(
        DbContext context,
        IEntityType entityType,
        TScope scope
    )
    {
        _context = context;
        _entityType = entityType;
        _scope = scope;
    }

    /// <summary>Moves a subtree beneath an existing parent in another active tree.</summary>
    internal Task MoveToAsync(
        TKey sourceKey,
        TTreeId sourceTreeId,
        TKey parentKey,
        TTreeId targetTreeId,
        CancellationToken cancellationToken
    )
    {
        var sourceStore = Store(sourceTreeId);
        var targetStore = Store(targetTreeId);
        var requests = new[]
        {
            sourceStore.LockRequest(NestedSetTreeLockMode.Existing),
            targetStore.LockRequest(NestedSetTreeLockMode.Existing),
        };

        var executor = new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(_context, _entityType);

        return executor.ExecuteAsync(
            token => MoveToCoreAsync(sourceStore, targetStore, sourceKey, parentKey, token),
            requests,
            cancellationToken);
    }

    /// <summary>Moves between trees already locked by the coordinated save's outer transaction.</summary>
    internal Task MoveToWithinSaveAsync(
        TKey sourceKey,
        TTreeId sourceTreeId,
        TKey parentKey,
        TTreeId targetTreeId,
        CancellationToken cancellationToken
    ) => NestedSetSaveChanges.IsManagedMutation(_context)
        // WHY: The outer SaveChanges transaction owns both registry locks and the rollback boundary. Reusing
        // the transfer algorithm directly avoids acquiring more locks or creating another savepoint per parent.
        ? MoveToCoreAsync(Store(sourceTreeId), Store(targetTreeId), sourceKey, parentKey, cancellationToken)
        : throw new InvalidOperationException("The coordinated save must own both tree mutation locks.");

    /// <summary>Transfers one subtree after both exact tree identities are protected.</summary>
    private async Task MoveToCoreAsync(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> sourceStore,
        NestedSetStore<TEntity, TKey, TTreeId, TScope> targetStore,
        TKey sourceKey,
        TKey parentKey,
        CancellationToken cancellationToken
    )
    {
        var source = await sourceStore
            .FindAsync(sourceKey, cancellationToken)
            .ConfigureAwait(false);

        var parent = await targetStore
            .FindAsync(parentKey, cancellationToken)
            .ConfigureAwait(false);

        NestedSetGuards.RequireBounds(source);
        NestedSetGuards.RequireBounds(parent);

        var width = checked(source.Right - source.Left + 1);
        var maximum = await targetStore
            .MaximumAsync(cancellationToken)
            .ConfigureAwait(false);

        _ = checked(maximum + width);

        await RequireDepthCapacityAsync(sourceStore, source, checked(parent.Depth + 1), cancellationToken)
            .ConfigureAwait(false);

        var destination = await new NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>(targetStore)
            .ResolveAsync(parentKey, NestedSetPlacement.LastChild, 0, cancellationToken, parent)
            .ConfigureAwait(false);

        await targetStore
            .ShiftBoundsAsync(destination.Boundary, width, cancellationToken)
            .ConfigureAwait(false);

        await RemoveSourceSiblingSlotAsync(sourceStore, source, cancellationToken)
            .ConfigureAwait(false);

        await sourceStore
            .SetParentAsync(
                sourceStore.Nodes.Where(sourceStore.Equal(sourceStore.Map.Key, source.Key)),
                new NestedSetParent<TKey>(true, parent.Key),
                cancellationToken)
            .ConfigureAwait(false);

        await sourceStore
            .SetLongAsync(
                sourceStore.Nodes.Where(sourceStore.Equal(sourceStore.Map.Key, source.Key)),
                sourceStore.Map.Position,
                destination.Position,
                cancellationToken)
            .ConfigureAwait(false);

        await TransferAsync(
                sourceStore,
                source,
                targetStore.TreeId,
                checked(destination.Boundary - source.Left),
                checked(destination.Depth - source.Depth),
                cancellationToken)
            .ConfigureAwait(false);

        await sourceStore
            .ShiftBoundsAsync(checked(source.Right + 1), -width, cancellationToken)
            .ConfigureAwait(false);

        if (targetStore.Map.Order is not null
            && destination.Position > 0)
        {
            await new NestedSetOrderer<TEntity, TKey, TTreeId, TScope>(targetStore)
                .ReorderAsync(source.Key, cancellationToken)
                .ConfigureAwait(false);
        }

        await CompleteSourceLifecycleAsync(sourceStore, source, cancellationToken)
            .ConfigureAwait(false);

        await NestedSetTreeRegistryState
            .TouchAsync(_context, targetStore.LockRequest(NestedSetTreeLockMode.Existing), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Detaches a subtree as the root of a newly reserved tree.</summary>
    internal Task DetachAsync(
        TKey sourceKey,
        TTreeId sourceTreeId,
        TTreeId newTreeId,
        CancellationToken cancellationToken
    )
    {
        var sourceStore = Store(sourceTreeId);
        var targetStore = Store(newTreeId);
        var executor = new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(_context, _entityType);
        var requests = new[]
        {
            sourceStore.LockRequest(NestedSetTreeLockMode.Existing),
            targetStore.LockRequest(NestedSetTreeLockMode.New),
        };

        return executor.ExecuteAsync(
            async token =>
            {
                var source = await sourceStore
                    .FindAsync(sourceKey, token)
                    .ConfigureAwait(false);

                NestedSetGuards.RequireBounds(source);

                if (await targetStore
                        .Nodes
                        .AnyAsync(token)
                        .ConfigureAwait(false))
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidStructure,
                        "A newly reserved TreeId already contains hierarchy rows.");
                }

                var width = checked(source.Right - source.Left + 1);
                await RemoveSourceSiblingSlotAsync(sourceStore, source, token)
                    .ConfigureAwait(false);

                var root = sourceStore.Nodes.Where(sourceStore.Equal(sourceStore.Map.Key, source.Key));
                await sourceStore
                    .SetParentAsync(root, default, token)
                    .ConfigureAwait(false);

                await sourceStore
                    .SetLongAsync(root, sourceStore.Map.Position, 0, token)
                    .ConfigureAwait(false);

                await TransferAsync(
                        sourceStore,
                        source,
                        targetStore.TreeId,
                        checked(1 - source.Left),
                        checked(-source.Depth),
                        token)
                    .ConfigureAwait(false);

                await sourceStore
                    .ShiftBoundsAsync(checked(source.Right + 1), -width, token)
                    .ConfigureAwait(false);

                await CompleteSourceLifecycleAsync(sourceStore, source, token)
                    .ConfigureAwait(false);
            },
            requests,
            cancellationToken);
    }

    /// <summary>Moves all subtree rows to the target identity while preserving relative intervals.</summary>
    private static async Task TransferAsync(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> sourceStore,
        NestedSetNode<TKey> source,
        TTreeId targetTreeId,
        long coordinateOffset,
        int depthOffset,
        CancellationToken cancellationToken
    )
    {
        var subtree = sourceStore.Nodes.Where(node =>
            EF.Property<long>(node, sourceStore.Map.Left) >= source.Left
            && EF.Property<long>(node, sourceStore.Map.Right) <= source.Right);

        await NestedSetTelemetry
            .TrackRows(
                subtree.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(sourceStore.Property<TTreeId>(sourceStore.Map.TreeId), targetTreeId)
                        .SetProperty(
                            sourceStore.Property<long>(sourceStore.Map.Left),
                            node => EF.Property<long>(node, sourceStore.Map.Left) + coordinateOffset)
                        .SetProperty(
                            sourceStore.Property<long>(sourceStore.Map.Right),
                            node => EF.Property<long>(node, sourceStore.Map.Right) + coordinateOffset)
                        .SetProperty(
                            sourceStore.Property<int>(sourceStore.Map.Depth),
                            node => EF.Property<int>(node, sourceStore.Map.Depth) + depthOffset),
                    cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>Closes the source root's former sibling slot before its TreeId changes.</summary>
    private static Task<int> RemoveSourceSiblingSlotAsync(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> sourceStore,
        NestedSetNode<TKey> source,
        CancellationToken cancellationToken
    ) => sourceStore.ChangeLongAsync(
        sourceStore
            .Siblings(source.Parent)
            .Where(node => EF.Property<long>(node, sourceStore.Map.Position) > source.Position),
        sourceStore.Map.Position,
        1,
        -1,
        cancellationToken);

    /// <summary>Rejects a depth translation that would overflow any moved descendant.</summary>
    private static async Task RequireDepthCapacityAsync(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> sourceStore,
        NestedSetNode<TKey> source,
        int destinationDepth,
        CancellationToken cancellationToken
    )
    {
        var delta = checked(destinationDepth - source.Depth);

        if (delta <= 0)
        {
            return;
        }

        var maximum = await sourceStore
            .Nodes
            .Where(node => EF.Property<long>(node, sourceStore.Map.Left) >= source.Left
                && EF.Property<long>(node, sourceStore.Map.Right) <= source.Right)
            .MaxAsync(node => EF.Property<int>(node, sourceStore.Map.Depth), cancellationToken)
            .ConfigureAwait(false);

        _ = checked(maximum + delta);
    }

    /// <summary>Touches a surviving source tree or tombstones an emptied source identity.</summary>
    private async Task CompleteSourceLifecycleAsync(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> sourceStore,
        NestedSetNode<TKey> source,
        CancellationToken cancellationToken
    )
    {
        var request = sourceStore.LockRequest(NestedSetTreeLockMode.Existing);

        if (source.Depth == 0)
        {
            await NestedSetTreeRegistryState
                .TombstoneAsync(_context, request, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await NestedSetTreeRegistryState
                .TouchAsync(_context, request, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Creates one exact-tree store over the shared mapped scope.</summary>
    private NestedSetStore<TEntity, TKey, TTreeId, TScope> Store(
        TTreeId treeId
    ) => new(_context, _entityType, _scope, treeId);
}
