namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    private static readonly MethodInfo s_takeAncestorOwner = MethodOf(
        (Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>>>)(owners => owners.Take(1)));

    /// <summary>Restricts independent nonleaf identities before exact native-key owner hydration.</summary>
    private IQueryable<TResult> SelectCoordinateAncestorPairs<TResult>(
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    )
    {
        var owner = Expression.Parameter(typeof(TEntity), "owner");
        var key = NestedSetExpressions.Property(owner, _descriptor.NodeKey.Resolve(_entityType), typeof(object));
        var eligible = NonLeafBounds(owner);
        var conditionalKey = Expression.Condition(eligible, key, Expression.Constant(null, typeof(object)));
        var coordinate = Expression.Lambda<Func<TEntity, AncestorBounds>>(
            Expression.MemberInit(
                Expression.New(typeof(AncestorBounds)),
                Expression.Bind(typeof(AncestorBounds).GetProperty(nameof(AncestorBounds.Key))!, conditionalKey),
                Coordinate(nameof(AncestorBounds.Tree), _descriptor.TreeId, typeof(object)),
                Coordinate(nameof(AncestorBounds.Left), _descriptor.Left),
                Coordinate(nameof(AncestorBounds.Right), _descriptor.Right)),
            owner);

        // WHY: A bare unique key lets SQL Server remove DISTINCT; a coordinate-only boundary let
        // its late payload hydration move before eligibility and repeat a full PK scan. The conditional
        // native key retains nonleaf classification while permitting an exact mapped-key hydration.
        // Every eligible owner keeps its own key, including owners sharing corrupt equal bounds.
        // Eligibility stays inside CASE: repeating it in WHERE would make the key branch unconditional.
        // WHY: Excluded intervals also share zero coordinates, so all leaves of one tree collapse
        // into one nonmatching class. Retaining their actual bounds kept the relation population-sized
        // even when only one root could be an ancestor. The NULL key cannot hydrate any required key.
        // WHY: EF cannot identify collection occurrences after DISTINCT over computed CASE values.
        // Grouping the same coordinate class lifts those values into columns first and gives EF
        // complete group identifiers; owned auto-includes and collection DTOs then remain one query.
        var coordinates = _nodes.GroupBy(coordinate).Select(group => group.Key);
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
            Expression.Equal(key, Expression.Property(bounds, nameof(AncestorBounds.Key))),
            Expression.Equal(
                NestedSetExpressions.Property(owner, _descriptor.TreeId.Resolve(_entityType), typeof(object)), tree));

        // WHY: Key and Tree keep native database equality, never a string conversion or CLR operator.
        // Both mapped roots retain the same scope, binding and filters; NULL ineligible keys cannot
        // match a required owner key, and every eligible equal-bound owner is restored separately.
        Expression mappedSource;

        if (_capabilities.UsesBoundedAncestorHydration)
        {
            // WHY: ValidateIdentity requires NodeKey or (Scope, NodeKey), and public composition has
            // already bound Scope. This is the exact zero-or-one identity lookup, not a result cap.
            // The same real containment already proved on owner-derived coordinates keeps its source
            // dependency inside the lookup. Retaining a mapped entity here preserves collection navigation.
            matches = Expression.AndAlso(matches, Contains(owner, sourceNode, includeSelf: false));
            var mappedOwners = Expression.Call(s_where, _nodes.Expression,
                Expression.Quote(Expression.Lambda<Func<TEntity, bool>>(matches, owner)));

            var order = Expression.Lambda<Func<TEntity, long>>(Bound(owner, _descriptor.Left), owner);
            mappedSource = Expression.Call(s_takeAncestorOwner,
                Expression.Call(s_orderByLeft, mappedOwners, Expression.Quote(order)), Expression.Constant(1));
        }
        else
        {
            mappedSource = Expression.Call(s_where, _nodes.Expression,
                Expression.Quote(Expression.Lambda<Func<TEntity, bool>>(matches, owner)));
        }

        var mapped = Expression.Lambda<Func<AncestorBoundsPair, IEnumerable<TEntity>>>(mappedSource, pair);

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
            Expression value = marker is null
                ? Expression.Condition(eligible,
                    NestedSetExpressions.QueryProperty(owner, property), Expression.Constant(0L))
                : NestedSetExpressions.Property(owner, property, marker);

            return Expression.Bind(typeof(AncestorBounds).GetProperty(member)!, value);
        }
    }

    /// <summary>Retains nonleaf eligibility and native identity without a bare unique-key projection.</summary>
    private sealed class AncestorBounds
    {
        /// <summary>Gets the native owner key for an eligible interval, or null for an ineligible one.</summary>
        public object? Key { get; init; }

        /// <summary>Gets the native mapped tree role used by the structural and hydration joins.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the eligible interval's left bound, or zero for an excluded class.</summary>
        public long Left { get; init; }

        /// <summary>Gets the eligible interval's right bound, or zero for an excluded class.</summary>
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
