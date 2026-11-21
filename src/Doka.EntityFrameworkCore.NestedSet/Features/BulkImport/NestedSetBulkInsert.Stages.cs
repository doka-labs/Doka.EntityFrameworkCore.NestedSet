namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Reserves and validates one tree interval while the caller owns its registry lock.</summary>
    /// <param name="plan">The validated detached branch and its compact structural snapshot.</param>
    /// <param name="parent">The existing parent key, or default for a new root.</param>
    /// <param name="placement">The destination placement, or null for a new root.</param>
    /// <param name="cancellationToken">The token used for database reads and interval reservation.</param>
    /// <returns>The locked destination whose geometry is shared by all insertion waves.</returns>
    internal async Task<NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination> PrepareDestinationAsync(
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> plan,
        TKey? parent,
        NestedSetPlacement? placement,
        CancellationToken cancellationToken
    )
    {
        if (placement is null
            && (plan.Roots.Count != 1
                || await _store
                    .Nodes
                    .AnyAsync(cancellationToken)
                    .ConfigureAwait(false)))
        {
            throw new NestedSetException(NestedSetErrorCode.InvalidStructure, "A TreeId can contain exactly one root.");
        }

        // WHY: A new root's exact tree is empty under its reserved registry lock. Only a child import needs
        // the existing maximum to verify capacity and whether its reserved interval shifts existing nodes.
        var maximum = placement is null
            ? 0
            : await _store
                .MaximumAsync(cancellationToken)
                .ConfigureAwait(false);

        _ = checked(maximum + plan.Width);

        var destination = await _placement
            .ResolveAsync(parent, placement, maximum, knownAnchor: null, cancellationToken)
            .ConfigureAwait(false);

        plan.RequireCapacity(destination);

        // WHY: Appending a new tree cannot affect existing coordinates. Inserting beneath a parent shifts the
        // remaining tree exactly once, independently of the imported node count.
        if (destination.Boundary <= maximum)
        {
            await _store
                .ShiftBoundsAsync(destination.Boundary, plan.Width, cancellationToken)
                .ConfigureAwait(false);
        }

        return destination;
    }

    /// <summary>Finalizes one persisted tree after its last bounded payload wave has completed.</summary>
    /// <param name="plan">The plan whose generated identities were captured after every payload save.</param>
    /// <param name="destination">The tree interval reserved before the first payload wave.</param>
    /// <param name="placement">The insertion placement, or null for a new tree.</param>
    /// <param name="cancellationToken">The token used for final parent, ordering and scalar refresh work.</param>
    internal async Task FinalizePreparedAsync(
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> plan,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination,
        NestedSetPlacement? placement,
        CancellationToken cancellationToken
    )
    {
        var persistence = new NestedSetBulkStore<TEntity, TKey, TTreeId, TScope>(_store, plan);

        if (_store.Map.Order is not null)
        {
            var ranks = await persistence
                .ReadRanksAsync(destination.Boundary, cancellationToken)
                .ConfigureAwait(false);

            plan.PrepareGeometry(destination, ranks, cancellationToken);
        }

        await persistence
            .FinalizeAsync(destination.Parent, cancellationToken)
            .ConfigureAwait(false);

        if (_store.Map.Order is not null)
        {
            await ReorderDestinationAsync(plan, destination.Parent, cancellationToken)
                .ConfigureAwait(false);
        }

        if (placement is not null)
        {
            await NestedSetTreeRegistryState
                .TouchAsync(_store.Context, _store.LockRequest(NestedSetTreeLockMode.Existing), cancellationToken)
                .ConfigureAwait(false);
        }

        // WHY: Every insertion batch is detached before finalization. Refresh therefore cannot populate mapped
        // navigation graphs and its working set remains bounded independently of total import size.
        await persistence
            .RefreshAsync(destination.Boundary, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Places imported roots using native order while preserving optional existing manual placements.
    /// </summary>
    private async Task ReorderDestinationAsync(
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> plan,
        NestedSetParent<TKey> parent,
        CancellationToken cancellationToken
    )
    {
        var order = _store.Map.Order!;
        var canonical = await order
            .Apply(_store.Siblings(parent))
            .Select(_store.Map.Projection)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (order.Mode == NestedSetOrderMode.Strict)
        {
            await new NestedSetSiblingReorderer<TEntity, TKey, TTreeId, TScope>(_store)
                .ReorderAsync(canonical, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        var imported = new HashSet<TKey>(plan.Roots.Select(index => plan.Nodes[index].Key), _store.Map.KeyComparer);

        var physical = new LinkedList<NestedSetNode<TKey>>(
            canonical
                .Where(node => !imported.Contains(node.Key))
                .OrderBy(node => node.Left));

        var links = new Dictionary<TKey, LinkedListNode<NestedSetNode<TKey>>>(_store.Map.KeyComparer);

        for (var link = physical.First; link is not null; link = link.Next)
        {
            links.Add(link.Value.Key, link);
        }

        LinkedListNode<NestedSetNode<TKey>>? predecessor = null;

        foreach (var node in canonical)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (imported.Contains(node.Key))
            {
                // WHY: Native predecessor placement matches flexible single insertion. Existing sibling nodes
                // stay linked in their current relative order even when that order deliberately overrides the rule.
                predecessor = predecessor is null
                    ? physical.AddFirst(node)
                    : physical.AddAfter(predecessor, node);

                links.Add(node.Key, predecessor);
            }
            else
            {
                predecessor = links[node.Key];
            }
        }

        await new NestedSetSiblingReorderer<TEntity, TKey, TTreeId, TScope>(_store)
            .ReorderAsync(physical.ToArray(), cancellationToken)
            .ConfigureAwait(false);
    }
}
