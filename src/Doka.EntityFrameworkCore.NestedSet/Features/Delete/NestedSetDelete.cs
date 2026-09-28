namespace Doka.EntityFrameworkCore.NestedSet.Features.Delete;

/// <summary>Deletes nodes and subtrees using exact-tree structural queries and atomic execution.</summary>
/// <typeparam name="TEntity">The mapped domain entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The scope type.</typeparam>
internal sealed class NestedSetDelete<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> _executor;

    /// <summary>Connects the algorithm to its exact-tree store and execution boundary.</summary>
    /// <param name="store">The shared tree-bound queries and structural writes.</param>
    /// <param name="executor">The transaction and lock coordinator.</param>
    internal NestedSetDelete(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> executor
    )
    {
        _store = store;
        _executor = executor;
    }

    /// <summary>Deletes a non-root node and promotes its children into its former sibling position.</summary>
    /// <param name="key">The persisted node key in the selected tree.</param>
    /// <param name="cancellationToken">The token used for registry locking and all structural changes.</param>
    /// <returns>A task that completes after the atomic deletion.</returns>
    internal Task DeleteAsync(
        TKey key,
        CancellationToken cancellationToken = default
    ) => _store.ExecuteMutationAsync(
        _executor,
        async token =>
        {
            var source = await _store
                .FindAsync(key, token)
                .ConfigureAwait(false);

            NestedSetGuards.RequireBounds(source);

            if (source.Depth == 0)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.OperationRejected,
                    "DeleteAsync cannot delete a tree root. Use DeleteSubtreeAsync or DeleteTreeAsync.");
            }

            var children = _store.Siblings(new NestedSetParent<TKey>(true, source.Key));
            var count = await children
                .CountAsync(token)
                .ConfigureAwait(false);

            // WHY: Promoted children occupy the deleted node's former slot as an ordered block. Following siblings
            // move by count minus one because that block replaces exactly one node in the original sibling group.
            await _store
                .ChangeLongAsync(
                    _store
                        .Siblings(source.Parent)
                        .Where(node => EF.Property<long>(node, _store.Map.Position) > source.Position),
                    _store.Map.Position,
                    1,
                    checked(count - 1),
                    token)
                .ConfigureAwait(false);

            if (count > 0)
            {
                await _store
                    .ChangeLongAsync(children, _store.Map.Position, 1, source.Position, token)
                    .ConfigureAwait(false);

                // WHY: Reparent before deletion so restrictive self-referencing foreign keys remain valid at each step.
                await _store
                    .SetParentAsync(children, source.Parent, token)
                    .ConfigureAwait(false);
            }

            var node = _store.Nodes.Where(_store.Equal(_store.Map.Key, source.Key));
            var physicalDelete = new NestedSetPhysicalDelete<TEntity, TKey, TTreeId, TScope>(_store);

            if (physicalDelete.IsRequired)
            {
                await physicalDelete
                    .ExecuteAsync(node, token)
                    .ConfigureAwait(false);
            }
            else
            {
                await NestedSetTelemetry
                    .TrackRows(node.ExecuteDeleteAsync(token))
                    .ConfigureAwait(false);
            }

            // WHY: A surviving descendant loses one ancestor and one coordinate on each side; later boundaries
            // lose both deleted coordinates. Depth must read the old interval before MySQL assigns either bound.
            await NestedSetTelemetry
                .TrackRows(
                    _store
                        .Nodes
                        .Where(node => EF.Property<long>(node, _store.Map.Right) > source.Left)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(
                                    _store.Property<int>(_store.Map.Depth),
                                    node => EF.Property<long>(node, _store.Map.Left) > source.Left
                                        && EF.Property<long>(node, _store.Map.Right) < source.Right
                                            ? EF.Property<int>(node, _store.Map.Depth) - 1
                                            : EF.Property<int>(node, _store.Map.Depth))
                                .SetProperty(
                                    _store.Property<long>(_store.Map.Left),
                                    node => EF.Property<long>(node, _store.Map.Left) > source.Right
                                        ? EF.Property<long>(node, _store.Map.Left) - 2
                                        : EF.Property<long>(node, _store.Map.Left) > source.Left
                                            ? EF.Property<long>(node, _store.Map.Left) - 1
                                            : EF.Property<long>(node, _store.Map.Left))
                                .SetProperty(
                                    _store.Property<long>(_store.Map.Right),
                                    node => EF.Property<long>(node, _store.Map.Right) > source.Right
                                        ? EF.Property<long>(node, _store.Map.Right) - 2
                                        : EF.Property<long>(node, _store.Map.Right) - 1),
                            token))
                .ConfigureAwait(false);

            if (count > 0
                && _store.Map.Order?.Mode == NestedSetOrderMode.Strict)
            {
                // WHY: A promoted block may interleave with its new siblings under the domain rule. Permuting
                // intervals in batches preserves every descendant without moving each promoted subtree separately.
                await new NestedSetOrderer<TEntity, TKey, TTreeId, TScope>(_store)
                    .ReorderSiblingsAsync(source.Parent, token)
                    .ConfigureAwait(false);
            }

            await NestedSetTreeRegistryState
                .TouchAsync(_store.Context, _store.LockRequest(NestedSetTreeLockMode.Existing), token)
                .ConfigureAwait(false);
        },
        NestedSetTreeLockMode.Existing,
        cancellationToken);

    /// <summary>Deletes one subtree and tombstones its registry identity when the subtree is the root.</summary>
    /// <param name="key">The subtree root key in the selected tree.</param>
    /// <param name="cancellationToken">The token used for registry locking and all structural changes.</param>
    /// <returns>A task that completes after the atomic deletion.</returns>
    internal Task DeleteSubtreeAsync(
        TKey key,
        CancellationToken cancellationToken = default
    ) => _store.ExecuteMutationAsync(
        _executor,
        async token =>
        {
            var source = await _store
                .FindAsync(key, token)
                .ConfigureAwait(false);

            NestedSetGuards.RequireBounds(source);

            var subtree = _store.Nodes.Where(node => EF.Property<long>(node, _store.Map.Left) >= source.Left
                && EF.Property<long>(node, _store.Map.Right) <= source.Right);

            var physicalDelete = new NestedSetPhysicalDelete<TEntity, TKey, TTreeId, TScope>(_store);

            if (physicalDelete.IsRequired
                || !NestedSetProviderCapabilities.Resolve(_store.Context)
                    .SupportsStatementAtomicSelfReferentialDelete)
            {
                // WHY: Doka and SQLite must tolerate restrictive self-FKs created outside the EF model.
                // Unknown physical constraints forbid a metadata-only shortcut; null roots need no unlink write.
                await _store
                    .SetParentAsync(subtree.Where(_store.HasParent()), default, token)
                    .ConfigureAwait(false);
            }

            if (physicalDelete.IsRequired)
            {
                await physicalDelete
                    .ExecuteAsync(subtree, token)
                    .ConfigureAwait(false);
            }
            else
            {
                await NestedSetTelemetry
                    .TrackRows(subtree.ExecuteDeleteAsync(token))
                    .ConfigureAwait(false);
            }

            await _store
                .ChangeLongAsync(
                    _store
                        .Siblings(source.Parent)
                        .Where(node => EF.Property<long>(node, _store.Map.Position) > source.Position),
                    _store.Map.Position,
                    1,
                    -1,
                    token)
                .ConfigureAwait(false);

            await _store
                .ShiftBoundsAsync(checked(source.Right + 1), -checked(source.Right - source.Left + 1), token)
                .ConfigureAwait(false);

            var request = _store.LockRequest(NestedSetTreeLockMode.Existing);

            if (source.Depth == 0)
            {
                await NestedSetTreeRegistryState
                    .TombstoneAsync(_store.Context, request, token)
                    .ConfigureAwait(false);
            }
            else
            {
                await NestedSetTreeRegistryState
                    .TouchAsync(_store.Context, request, token)
                    .ConfigureAwait(false);
            }
        },
        NestedSetTreeLockMode.Existing,
        cancellationToken);
}
