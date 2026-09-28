namespace Doka.EntityFrameworkCore.NestedSet.Features.Move;

/// <summary>Moves one existing subtree while its caller owns the transaction and hierarchy write lock.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The optional mapped scope type or the scope-free marker.</typeparam>
internal sealed class NestedSetSubtreeMover<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;

    /// <summary>Uses the existing caller-owned context and tree-bound structural queries.</summary>
    /// <param name="store">The store protected by the caller's write lock.</param>
    internal NestedSetSubtreeMover(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store
    )
    {
        _store = store;
    }

    /// <summary>Applies a validated destination using current, locked source coordinates.</summary>
    /// <param name="source">The source snapshot read after acquiring the write lock.</param>
    /// <param name="destination">The destination coordinates before removing the source interval.</param>
    /// <param name="token">The cancellation token for structural reads and writes.</param>
    /// <returns>Whether the move performed structural writes.</returns>
    /// <remarks>The caller validates ancestry and supplies the atomic transaction and lock boundary.</remarks>
    internal async Task<bool> MoveAsync(
        NestedSetNode<TKey> source,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination,
        CancellationToken token
    )
    {
        // WHY: Removing the source first shifts destinations to its right and later positions in the same group.
        var width = checked(source.Right - source.Left + 1);
        var boundary = destination.Boundary > source.Right
            ? checked(destination.Boundary - width)
            : destination.Boundary;

        // WHY: Parent identity must use database key equality, including collation, rather than CLR equality.
        var sameParent = await _store
            .Siblings(destination.Parent)
            .AnyAsync(_store.Equal(_store.Map.Key, source.Key), token)
            .ConfigureAwait(false);

        var position = sameParent && source.Position < destination.Position
            ? checked(destination.Position - 1)
            : destination.Position;

        var depthDelta = checked(destination.Depth - source.Depth);

        if (boundary == source.Left
            && sameParent
            && position == source.Position
            && depthDelta == 0)
        {
            return false;
        }

        if (depthDelta > 0)
        {
            // WHY: A deeper destination can overflow a descendant's depth even when the moved root still fits.
            var maximumDepth = await _store
                .Nodes
                .Where(node => EF.Property<long>(node, _store.Map.Left) >= source.Left
                    && EF.Property<long>(node, _store.Map.Right) <= source.Right)
                .MaxAsync(node => EF.Property<int>(node, _store.Map.Depth), token)
                .ConfigureAwait(false);

            _ = checked(maximumDepth + depthDelta);
        }

        if (sameParent)
        {
            // WHY: Only crossed siblings change position; later siblings retain their slot and must not be rewritten.
            var minimum = Math.Min(source.Position, position);
            var maximum = Math.Max(source.Position, position);
            await _store
                .ChangeLongAsync(
                    _store
                        .Siblings(source.Parent)
                        .Where(node => EF.Property<long>(node, _store.Map.Position) >= minimum
                            && EF.Property<long>(node, _store.Map.Position) <= maximum)
                        .Where(node => EF.Property<long>(node, _store.Map.Left) != source.Left),
                    _store.Map.Position,
                    1,
                    position < source.Position ? 1 : -1,
                    token)
                .ConfigureAwait(false);
        }
        else
        {
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

            if (!destination.Append)
            {
                await _store
                    .ChangeLongAsync(
                        _store
                            .Siblings(destination.Parent)
                            .Where(node => EF.Property<long>(node, _store.Map.Position) >= position),
                        _store.Map.Position,
                        1,
                        1,
                        token)
                    .ConfigureAwait(false);
            }
        }

        var offset = checked(boundary - source.Left);
        var left = source.Left;
        var right = source.Right;
        var destinationBoundary = destination.Boundary;
        var minimumBoundary = Math.Min(left, destinationBoundary);
        var maximumBoundary = Math.Max(right, checked(destinationBoundary - 1));

        // WHY: Relocation is a permutation of only the source and crossed intervals. A distant tail has zero net
        // displacement and is excluded entirely. Depth reads the original bounds before either coordinate changes.
        await NestedSetTelemetry
            .TrackRows(
                _store
                    .Nodes
                    .Where(node =>
                        (EF.Property<long>(node, _store.Map.Left) >= minimumBoundary
                            && EF.Property<long>(node, _store.Map.Left) <= maximumBoundary)
                        || (EF.Property<long>(node, _store.Map.Right) >= minimumBoundary
                            && EF.Property<long>(node, _store.Map.Right) <= maximumBoundary))
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                _store.Property<int>(_store.Map.Depth),
                                node =>
                                    EF.Property<long>(node, _store.Map.Left) >= left
                                    && EF.Property<long>(node, _store.Map.Right) <= right
                                        ? EF.Property<int>(node, _store.Map.Depth) + depthDelta
                                        : EF.Property<int>(node, _store.Map.Depth))
                            .SetProperty(
                                _store.Property<long>(_store.Map.Left),
                                RelocateBoundary(_store.Map.Left, source, destinationBoundary, offset))
                            .SetProperty(
                                _store.Property<long>(_store.Map.Right),
                                RelocateBoundary(_store.Map.Right, source, destinationBoundary, offset)),
                        token))
            .ConfigureAwait(false);

        // WHY: Descendant parent links and positions remain valid; only the relocated root changes sibling group.
        var moved = _store.Nodes.Where(_store.Equal(_store.Map.Key, source.Key));
        if (!sameParent)
        {
            await _store
                .SetParentAsync(moved, destination.Parent, token)
                .ConfigureAwait(false);
        }

        if (position != source.Position)
        {
            // WHY: Cross-parent moves can retain the same ordinal; avoid firing another update for that known no-op.
            await _store
                .SetLongAsync(moved, _store.Map.Position, position, token)
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Permutes one coordinate using only that coordinate's old value.</summary>
    /// <param name="property">The independently assigned boundary column.</param>
    /// <param name="source">The original source interval.</param>
    /// <param name="destination">The insertion point before removing the source.</param>
    /// <param name="offset">The final displacement applied to source coordinates.</param>
    /// <returns>A parameterized CASE expression shared by left and right assignments.</returns>
    private static Expression<Func<TEntity, long>> RelocateBoundary(
        string property,
        NestedSetNode<TKey> source,
        long destination,
        long offset
    )
    {
        var left = source.Left;
        var right = source.Right;
        var width = checked(right - left + 1);

        // WHY: Each setter reads only its own boundary, so the expression is safe under MySQL assignment order.
        return node => EF.Property<long>(node, property) >= left && EF.Property<long>(node, property) <= right
            ? EF.Property<long>(node, property) + offset
            : destination < left
            && EF.Property<long>(node, property) >= destination
            && EF.Property<long>(node, property) < left
                ? EF.Property<long>(node, property) + width
                : destination > right
                && EF.Property<long>(node, property) > right
                && EF.Property<long>(node, property) < destination
                    ? EF.Property<long>(node, property) - width
                    : EF.Property<long>(node, property);
    }
}
