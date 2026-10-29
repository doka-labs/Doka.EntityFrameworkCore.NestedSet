namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Retains only this immutable query binding's deferred coordinate-width expression.</summary>
    private Expression? _coordinateBlockWidth;

    private static readonly MethodInfo s_maximumNullableCoordinate = MethodOf(
        (Expression<Func<IQueryable<long?>, long?>>)(coordinates => coordinates.Max()));

    private static readonly Expression<Func<IQueryable<TEntity>, string, long>> s_maximumVisibleRight = (
        nodes,
        right
    ) => nodes.Max(node => (long?)EF.Property<long>(node, right)) ?? 0L;

    private static readonly Expression<Func<long, long>> s_coordinateBlockWidth =
        maximum => (long)Math.Floor(Math.Sqrt(maximum)) + 1L;

    private static readonly Expression<Func<long, long, long>> s_coordinateBlockStart = (
        left,
        width
    ) => left - left % width;

    /// <summary>Builds a positive adaptive partition width from the maximum visible Int64 boundary.</summary>
    internal static Expression CoordinateBlockWidth(
        Expression maximumVisibleRight
    ) => NestedSetQueryExpressions.Inline(s_coordinateBlockWidth, maximumVisibleRight);

    /// <summary>Builds an exact block start without adding a width near the Int64 boundary.</summary>
    internal static Expression CoordinateBlockStart(
        Expression left,
        Expression width
    ) => NestedSetQueryExpressions.Inline(s_coordinateBlockStart, left, width);

    /// <summary>Uses the same visible root to size candidate and invalid-child coordinate partitions.</summary>
    private Expression VisibleCoordinateBlockWidth()
        // WHY: Nearest owners and their completeness proofs use the identical visible root. Reuse its
        // expression, never an evaluated width; a new scope/tree binding owns a fresh cache and SQL sees current data.
        => _coordinateBlockWidth ??= CoordinateBlockWidth(
            NestedSetQueryExpressions.Inline(
                s_maximumVisibleRight,
                _nodes.Expression,
                Expression.Constant(_descriptor.Right.Name)));

    /// <summary>Resolves a visible endpoint before joining any mapped scope/tree coordinate partition.</summary>
    private MethodCallExpression VisibleCoordinateEndpoint(
        Expression selected
    )
    {
        var endpoint = Expression.Parameter(typeof(TEntity), "coordinateEndpoint");

        return Expression.Call(
            s_where,
            _nodes.Expression,
            Expression.Quote(Expression.Lambda<Func<TEntity, bool>>(MatchEndpoint(endpoint, selected), endpoint)));
    }
}
