namespace Doka.EntityFrameworkCore.NestedSet.Features.Move;

/// <summary>Relocates one subtree inside an exact tree whose caller owns the transaction and registry lock.</summary>
/// <typeparam name="TEntity">The mapped domain entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TScope">The optional mapped scope type or the scope-free marker.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
internal sealed class NestedSetMove<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope> _placement;

    /// <summary>Connects the algorithm to the exact-tree store protected by its caller.</summary>
    /// <param name="store">The shared tree-bound queries and structural writes.</param>
    /// <param name="placement">The destination resolver shared with other mutation algorithms.</param>
    internal NestedSetMove(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope> placement
    )
    {
        // WHY: Both facade mutations and coordinated saves establish their own atomic boundary. The common
        // algorithm requires no executor allocation, mode flag, additional savepoint, or repeated registry lock.
        _store = store;
        _placement = placement;
    }

    /// <summary>Moves under the coordinated save's already held transaction and exact-tree registry lock.</summary>
    /// <param name="key">The subtree root key.</param>
    /// <param name="parent">The destination parent key in the same tree.</param>
    /// <param name="cancellationToken">The token used for all structural reads and writes.</param>
    internal Task MoveToWithinSaveAsync(
        TKey key,
        TKey parent,
        CancellationToken cancellationToken
    ) => NestedSetSaveChanges.IsManagedMutation(_store.Context)
        ? ExecuteAsync(key, parent, NestedSetPlacement.LastChild, automatic: true, cancellationToken)
        : throw new InvalidOperationException("The coordinated save must own the exact-tree mutation lock.");

    /// <summary>Relocates a subtree after its caller established rollback and exact-tree lock ownership.</summary>
    /// <param name="key">The subtree root key in this tree.</param>
    /// <param name="anchor">The destination parent or sibling key in this tree.</param>
    /// <param name="placement">The anchor-relative placement.</param>
    /// <param name="automatic">Whether configured ordering determines the destination sibling position.</param>
    /// <param name="cancellationToken">The token used for all database work.</param>
    /// <returns>A task that completes after the locked structural changes.</returns>
    internal async Task ExecuteAsync(
        TKey key,
        TKey anchor,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    )
    {
        if (!automatic)
        {
            NestedSetOrderer<TEntity, TKey, TTreeId, TScope>.RequireManualPlacement(_store.Map.Order);
        }

        var source = await _store
            .FindAsync(key, cancellationToken)
            .ConfigureAwait(false);

        NestedSetGuards.RequireBounds(source);

        var target = await _store
            .FindAsync(anchor, cancellationToken)
            .ConfigureAwait(false);

        NestedSetGuards.RequireBounds(target);

        if (target.Left >= source.Left
            && target.Right <= source.Right)
        {
            throw new NestedSetException(
                NestedSetErrorCode.CycleDetected,
                "A node cannot be moved relative to itself or into its subtree.");
        }

        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination;

        if (automatic && _store.Map.Order is not null)
        {
            // WHY: Reuse the locked parent snapshot and its canonical database key for ranking.
            destination = await new NestedSetOrderer<TEntity, TKey, TTreeId, TScope>(_store)
                .ResolveAsync(source.Key, new NestedSetParent<TKey>(true, target.Key), cancellationToken, target)
                .ConfigureAwait(false);
        }
        else
        {
            destination = await _placement
                .ResolveAsync(anchor, placement, 0, cancellationToken, target)
                .ConfigureAwait(false);
        }

        var changed = await new NestedSetSubtreeMover<TEntity, TKey, TTreeId, TScope>(_store)
            .MoveAsync(source, destination, cancellationToken)
            .ConfigureAwait(false);

        if (changed)
        {
            await NestedSetTreeRegistryState
                .TouchAsync(_store.Context, _store.LockRequest(NestedSetTreeLockMode.Existing), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
