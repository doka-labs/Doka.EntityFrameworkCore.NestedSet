namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Restricts independent nonleaf coordinates before restoring every visible mapped owner.</summary>
    private IQueryable<TResult> SelectCoordinateAncestorPairs<TResult>(
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    )
    {
        var owner = Expression.Parameter(typeof(TEntity), "owner");
        var nonLeaf = Expression.Lambda<Func<TEntity, bool>>(NonLeafBounds(owner), owner);
        var coordinate = Expression.Lambda<Func<TEntity, AncestorBounds>>(
            Expression.MemberInit(
                Expression.New(typeof(AncestorBounds)),
                Coordinate(nameof(AncestorBounds.Tree), _descriptor.TreeId, typeof(object)),
                Coordinate(nameof(AncestorBounds.Left), _descriptor.Left),
                Coordinate(nameof(AncestorBounds.Right), _descriptor.Right)),
            owner);

        // WHY: A unique node key lets SQL Server remove DISTINCT and repeat the whole ancestor
        // range for every source. Coordinates alone preserve a real independent structural relation;
        // they do not deduplicate the mapped owners or any caller occurrence returned below.
        var coordinates = _nodes.Where(nonLeaf).Select(coordinate).Distinct();
        var collection = Expression.Lambda<Func<TEntity, IEnumerable<AncestorBounds>>>(
            coordinates.Expression,
            Expression.Parameter(typeof(TEntity), "source"));

        var ranges = _nodes.Where(selected).SelectMany(collection,
            (source, bounds) => new AncestorBoundsPair { Source = source, Bounds = bounds });

        var pair = Expression.Parameter(typeof(AncestorBoundsPair), "pair");
        var sourceNode = Expression.Property(pair, nameof(AncestorBoundsPair.Source));
        var bounds = Expression.Property(pair, nameof(AncestorBoundsPair.Bounds));
        var tree = Expression.Property(bounds, nameof(AncestorBounds.Tree));
        var left = Expression.Property(bounds, nameof(AncestorBounds.Left));
        var right = Expression.Property(bounds, nameof(AncestorBounds.Right));
        var containment = ContainedBounds(left, right,
            Bound(sourceNode, _descriptor.Left), Bound(sourceNode, _descriptor.Right), includeSelf: false);

        if (!_treeBound)
        {
            containment = Expression.AndAlso(
                Expression.Equal(tree,
                    NestedSetExpressions.Property(sourceNode, _descriptor.TreeId.Resolve(_entityType), typeof(object))),
                containment);
        }

        var onPath = Expression.Lambda<Func<AncestorBoundsPair, bool>>(containment, pair);
        var matches = Expression.AndAlso(
            Expression.Equal(
                NestedSetExpressions.Property(owner, _descriptor.TreeId.Resolve(_entityType), typeof(object)), tree),
            Expression.AndAlso(
                Expression.Equal(Bound(owner, _descriptor.Left), left),
                Expression.Equal(Bound(owner, _descriptor.Right), right)));

        // WHY: Distinct coordinates may represent several visible owners after external corruption.
        // Rejoining all three native roles restores every original owner, including duplicate bounds,
        // while both roots retain the exact scope, tree binding, mapping, collation and query filters.
        var mapped = Expression.Lambda<Func<AncestorBoundsPair, IEnumerable<TEntity>>>(
            Expression.Call(s_where, _nodes.Expression,
                Expression.Quote(Expression.Lambda<Func<TEntity, bool>>(matches, owner))),
            pair);

        var pairs = ranges.Where(onPath).SelectMany(mapped,
            (range, related) => new { range.Source, Related = related });

        var source = Expression.Parameter(typeof(TEntity), "source");
        var ancestor = Expression.Parameter(typeof(TEntity), "ancestor");
        var distance = Expression.Parameter(typeof(int), "distance");
        var upwardProjection = Expression.Lambda<Func<TEntity, TEntity, int, TResult>>(
            NestedSetQueryExpressions.Inline(projection, source, ancestor, distance),
            ancestor, source, distance);

        return ProjectPairs(pairs, value => value.Related, value => value.Source, upwardProjection);

        MemberAssignment Coordinate(string member, NestedSetPropertyDescriptor descriptor, Type? marker = null)
        {
            var property = descriptor.Resolve(_entityType);
            var value = marker is null
                ? NestedSetExpressions.QueryProperty(owner, property)
                : NestedSetExpressions.Property(owner, property, marker);

            return Expression.Bind(typeof(AncestorBounds).GetProperty(member)!, value);
        }
    }

    /// <summary>Retains native structural equality without including the mapped owner's unique key.</summary>
    private sealed class AncestorBounds
    {
        /// <summary>Gets the native mapped tree role used by the structural and hydration joins.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the independent left bound.</summary>
        public long Left { get; init; }

        /// <summary>Gets the independent right bound.</summary>
        public long Right { get; init; }
    }

    /// <summary>Retains one source occurrence and a matched coordinate class until mapped owner hydration.</summary>
    private sealed class AncestorBoundsPair
    {
        /// <summary>Gets the actual visible source alias selected by the caller.</summary>
        public required TEntity Source { get; init; }

        /// <summary>Gets the structural class whose original owners are restored without deduplicating them.</summary>
        public required AncestorBounds Bounds { get; init; }
    }
}
