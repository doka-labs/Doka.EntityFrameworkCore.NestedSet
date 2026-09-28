namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal sealed partial class NestedSetSaveGroup<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    // WHY: The immutable DTO constructor is shared by typed projections; no reflection is repeated per key batch.
    private static readonly ConstructorInfo s_reorderIdentity = typeof(ReorderIdentity).GetConstructors()[0];

    /// <inheritdoc />
    internal override async Task ReorderAsync(
        CancellationToken cancellationToken
    )
    {
        if (_map.Order is null)
        {
            return;
        }

        var movedByScope = new Dictionary<TScope, HashSet<TKey>>(_scopes.Comparer);

        foreach (var change in _parentChanges)
        {
            if (!movedByScope.TryGetValue(change.SourceScope, out var movedKeys))
            {
                movedKeys = new HashSet<TKey>(_map.KeyComparer);
                movedByScope.Add(change.SourceScope, movedKeys);
            }

            movedKeys.Add(change.Key);
        }

        // WHY: Parent moves have already ranked their destination. A scope/key index avoids scanning all
        // requested moves for every changed identity while the tree locks are held.
        var reorderIdentities = _identities
            .Where(identity => !movedByScope.TryGetValue(identity.Scope, out var movedKeys)
                || !movedKeys.Contains(identity.Key))
            .ToArray();

        if (reorderIdentities.Length == 0)
        {
            // WHY: Automatic Parent moves rank the node after the payload save, so a second reorder would be
            // redundant and a cross-tree move would still carry the no-longer-current source TreeId.
            return;
        }

        foreach (var scope in reorderIdentities.GroupBy(identity => identity.Scope, _scopes.Comparer))
        {
            var source = Nodes();

            if (_map.Scope is not null)
            {
                source = source.Where(MatchesValues(_map.ScopeProperty!, [scope.Key]));
            }

            // WHY: Identities were projected from the database, so CLR key equality removes duplicate
            // aliases exactly before the keys are split into bounded batches.
            var keys = scope
                .Select(identity => identity.Key)
                .Distinct(_map.KeyComparer)
                .ToArray();

            if (_map.Order.Mode == NestedSetOrderMode.Strict)
            {
                await ReorderStrictAsync(scope.Key, source, keys, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ReorderPlacedAsync(_map.Order, scope.Key, source, keys, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Sorts each affected strict sibling group once after resolving changed identities in batches.</summary>
    /// <param name="scope">The tracked scope whose identities were resolved before locking.</param>
    /// <param name="source">The hierarchy restricted to that scope.</param>
    /// <param name="keys">The distinct persisted keys whose ordering values were saved.</param>
    /// <param name="cancellationToken">The token for bounded reads and structural updates.</param>
    private async Task ReorderStrictAsync(
        TScope scope,
        IQueryable<TEntity> source,
        TKey[] keys,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<ReorderIdentity>(keys.Length);
        var projection = ReorderProjection();

        foreach (var batch in keys.Chunk(NestedSetBatch.MaximumRows))
        {
            // WHY: Converted keys use one scalar parameter per value and have no provider collection fallback.
            rows.AddRange(
                await source
                    .Where(Matches(batch))
                    .Select(projection)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false));
        }

        foreach (var tree in rows.GroupBy(row => row.TreeId, _treeIdComparer))
        {
            var store = new NestedSetStore<TEntity, TKey, TTreeId, TScope>(_context, _map.EntityType, scope, tree.Key);
            var orderer = new NestedSetOrderer<TEntity, TKey, TTreeId, TScope>(store);
            var treeRows = tree.ToArray();

            // WHY: An absent parent is the root's sibling-group identity. Root payload changes still participate
            // in the same strict-save path without pretending that a tree contains several sibling roots.
            if (treeRows.Any(row => !row.Parent.HasValue))
            {
                AddIntervals(
                    scope,
                    tree.Key,
                    await orderer
                        .ReorderSiblingsAsync(default, cancellationToken)
                        .ConfigureAwait(false));
            }

            foreach (var parent in treeRows
                         .Where(row => row.Parent.HasValue)
                         .GroupBy(row => row.Parent.Value, _map.KeyComparer))
            {
                AddIntervals(
                    scope,
                    tree.Key,
                    await orderer
                        .ReorderSiblingsAsync(new NestedSetParent<TKey>(true, parent.Key), cancellationToken)
                        .ConfigureAwait(false));
            }
        }
    }

    /// <summary>Projects only the typed tree and optional parent needed to deduplicate strict sibling groups.</summary>
    private Expression<Func<TEntity, ReorderIdentity>> ReorderProjection()
    {
        var node = Expression.Parameter(typeof(TEntity), "node");
        var tree = NestedSetExpressions.Property(node, _map.TreeId, typeof(TTreeId));
        var parent = NestedSetParent<TKey>.Property(node, _map.ParentProperty);

        return Expression.Lambda<Func<TEntity, ReorderIdentity>>(Expression.New(s_reorderIdentity, tree, parent), node);
    }

    /// <summary>Places each changed node after its rule predecessor, in rule order within each tree.</summary>
    /// <param name="order">The configured rule that allows manual placement.</param>
    /// <param name="scope">The tracked scope whose identities were resolved before locking.</param>
    /// <param name="source">The hierarchy restricted to that scope.</param>
    /// <param name="keys">The distinct persisted keys whose ordering values were saved.</param>
    /// <param name="cancellationToken">The token for bounded reads and structural updates.</param>
    private async Task ReorderPlacedAsync(
        NestedSetOrdering order,
        TScope scope,
        IQueryable<TEntity> source,
        TKey[] keys,
        CancellationToken cancellationToken
    )
    {
        var trees = new Dictionary<TTreeId, PlacedTree>(_treeIdComparer);
        var batchIndex = 0;

        foreach (var batch in keys.Chunk(NestedSetBatch.MaximumRows))
        {
            // WHY: Converted keys use one scalar parameter per value and have no provider collection fallback.
            var rows = await order
                .Apply(source.Where(Matches(batch)))
                .Select(node => new PlacedIdentity(
                    EF.Property<TKey>(node, _map.Key),
                    EF.Property<TTreeId>(node, _map.TreeId)))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                if (!trees.TryGetValue(row.TreeId, out var tree))
                {
                    tree = new PlacedTree(batchIndex);
                    trees.Add(row.TreeId, tree);
                }

                tree.Add(row.Key, batchIndex);
            }

            batchIndex++;
        }

        foreach (var (treeId, tree) in trees)
        {
            var store = new NestedSetStore<TEntity, TKey, TTreeId, TScope>(_context, _map.EntityType, scope, treeId);

            var orderer = new NestedSetOrderer<TEntity, TKey, TTreeId, TScope>(store);
            var ordered = tree.SpansBatches
                ? await OrderPlacedTreeAsync(order, store, tree.Keys, cancellationToken).ConfigureAwait(false)
                : tree.Keys;

            // WHY: Each placement reads the current rule predecessor. Changed siblings must therefore move in
            // rule order, or a later move can separate a node from a predecessor placed before it.
            foreach (var key in ordered)
            {
                AddIntervals(
                    scope,
                    treeId,
                    await orderer
                        .ReorderAsync(key, cancellationToken)
                        .ConfigureAwait(false));
            }
        }
    }

    /// <summary>Restores one database rule order for a tree whose changed keys spanned several key batches.</summary>
    /// <param name="order">The configured rule that allows manual placement.</param>
    /// <param name="store">The exact locked tree.</param>
    /// <param name="keys">The persisted changed keys in that tree.</param>
    /// <param name="cancellationToken">The token for the bounded or streamed ordering read.</param>
    /// <returns>The changed keys in configured sibling order.</returns>
    private async Task<IReadOnlyList<TKey>> OrderPlacedTreeAsync(
        NestedSetOrdering order,
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        List<TKey> keys,
        CancellationToken cancellationToken
    )
    {
        if (keys.Count <= NestedSetBatch.MaximumRows)
        {
            return await order
                .Apply(store.Nodes.Where(Matches(keys.ToArray())))
                .Select(node => EF.Property<TKey>(node, _map.Key))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // WHY: A key filter would exceed the batch bound. Streaming the tree's keys in rule order keeps one
        // database ordering; both key sets are persisted representations, so the CLR lookup is exact.
        var pending = new HashSet<TKey>(keys, _map.KeyComparer);
        var ordered = new List<TKey>(keys.Count);

        await foreach (var key in order
                           .Apply(store.Nodes)
                           .Select(node => EF.Property<TKey>(node, _map.Key))
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (pending.Remove(key))
            {
                ordered.Add(key);
            }
        }

        return ordered;
    }
}
