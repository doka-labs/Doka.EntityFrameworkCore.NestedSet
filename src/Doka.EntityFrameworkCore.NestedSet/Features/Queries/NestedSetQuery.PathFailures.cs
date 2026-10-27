namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    // WHY: Separate scoped/unscoped templates preserve mapped native identity joins and omit absent
    // scope roles entirely. All templates retain only expression structure and public method metadata;
    // no context, model, request, caller row or compiled delegate is held in static state.
    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string, string,
            IQueryable<long?>>> s_scopedInteriorFailureRights = (
            endpoints,
            failures,
            width,
            upperBlock,
            lowerBlock,
            scope,
            tree,
            left,
            right
        ) => endpoints
            .Join(
                failures
                    .GroupBy(child => new
                    {
                        Scope = EF.Property<object>(child, scope),
                        Tree = EF.Property<object>(child, tree),
                        Block = EF.Property<long>(child, left) - EF.Property<long>(child, left) % width,
                    })
                    .Select(group => new
                    {
                        group.Key.Scope,
                        group.Key.Tree,
                        group.Key.Block,
                        Right = group.Max(child => EF.Property<long>(child, right)),
                    }),
                endpoint => new
                {
                    Scope = EF.Property<object>(endpoint, scope),
                    Tree = EF.Property<object>(endpoint, tree),
                },
                block => new
                {
                    block.Scope,
                    block.Tree,
                },
                (endpoint, block) => block)
            .Select(block => block.Block > upperBlock && block.Block < lowerBlock ? (long?)block.Right : null);

    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string,
            IQueryable<long?>>> s_unscopedInteriorFailureRights = (
            endpoints,
            failures,
            width,
            upperBlock,
            lowerBlock,
            tree,
            left,
            right
        ) => endpoints
            .Join(
                failures
                    .GroupBy(child => new
                    {
                        Tree = EF.Property<object>(child, tree),
                        Block = EF.Property<long>(child, left) - EF.Property<long>(child, left) % width,
                    })
                    .Select(group => new
                    {
                        group.Key.Tree,
                        group.Key.Block,
                        Right = group.Max(child => EF.Property<long>(child, right)),
                    }),
                endpoint => EF.Property<object>(endpoint, tree),
                block => block.Tree,
                (endpoint, block) => block)
            .Select(block => block.Block > upperBlock && block.Block < lowerBlock ? (long?)block.Right : null);

    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string, string,
            IQueryable<long?>>> s_scopedFringeFailureRights = (
            endpoints,
            failures,
            width,
            upperLeft,
            lowerLeft,
            scope,
            tree,
            left,
            right
        ) => endpoints
            .Join(
                failures
                    .GroupBy(child => new
                    {
                        Scope = EF.Property<object>(child, scope),
                        Tree = EF.Property<object>(child, tree),
                        Left = EF.Property<long>(child, left),
                        Right = EF.Property<long>(child, right),
                        Block = EF.Property<long>(child, left) - EF.Property<long>(child, left) % width,
                    })
                    .Select(group => group.Key),
                endpoint => new
                {
                    Scope = EF.Property<object>(endpoint, scope),
                    Tree = EF.Property<object>(endpoint, tree),
                    Block = EF.Property<long>(endpoint, left) - EF.Property<long>(endpoint, left) % width,
                },
                point => new
                {
                    point.Scope,
                    point.Tree,
                    point.Block,
                },
                (endpoint, point) => point)
            .Select(point => point.Left > upperLeft && point.Left <= lowerLeft ? (long?)point.Right : null);

    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string,
            IQueryable<long?>>> s_unscopedFringeFailureRights = (
            endpoints,
            failures,
            width,
            upperLeft,
            lowerLeft,
            tree,
            left,
            right
        ) => endpoints
            .Join(
                failures
                    .GroupBy(child => new
                    {
                        Tree = EF.Property<object>(child, tree),
                        Left = EF.Property<long>(child, left),
                        Right = EF.Property<long>(child, right),
                        Block = EF.Property<long>(child, left) - EF.Property<long>(child, left) % width,
                    })
                    .Select(group => group.Key),
                endpoint => new
                {
                    Tree = EF.Property<object>(endpoint, tree),
                    Block = EF.Property<long>(endpoint, left) - EF.Property<long>(endpoint, left) % width,
                },
                point => new
                {
                    point.Tree,
                    point.Block,
                },
                (endpoint, point) => point)
            .Select(point => point.Left > upperLeft && point.Left <= lowerLeft ? (long?)point.Right : null);

    /// <summary>Rejects paths whose interior blocks or endpoint fringes contain an invalid direct child.</summary>
    private BinaryExpression InvalidPathChildren(
        Expression upper,
        Expression lower,
        LambdaExpression? edgeAllowed,
        IQueryable<NestedSetQueryEdge<TEntity>> edges
    )
    {
        var invalid = Expression.Parameter(typeof(TEntity), "invalidChild");
        var edge = Expression.Parameter(typeof(NestedSetQueryEdge<TEntity>), "directEdge");
        var parent = Expression.Property(edge, s_edgeParent);
        var child = Expression.Property(edge, s_edgeChild);
        Expression valid = Expression.AndAlso(
            MatchKnownEndpoint(child, invalid),
            Expression.AndAlso(
                Contains(parent, child, includeSelf: false),
                Expression.Equal(
                    Expression.Convert(Depth(child), typeof(long)),
                    Expression.Add(Expression.Convert(Depth(parent), typeof(long)), s_oneEdge))));

        if (edgeAllowed is not null)
        {
            // WHY: Only row-independent rules enter this proof. Rebind the actual joined parent/child
            // aliases without inventing a row instance or evaluating any caller expression.
            valid = Expression.AndAlso(
                valid,
                NestedSetQueryExpressions.Inline(edgeAllowed, edgeAllowed.Parameters[0], parent, child));
        }

        var failures = _nodes.Where(
            Expression.Lambda<Func<TEntity, bool>>(Expression.Not(Any(edges, edge, valid)), invalid));

        var leftName = Expression.Constant(_descriptor.Left.Name);
        var rightName = Expression.Constant(_descriptor.Right.Name);
        var treeName = Expression.Constant(_descriptor.TreeId.Name);
        var width = VisibleCoordinateBlockWidth();
        var upperLeft = Bound(upper, _descriptor.Left);
        var lowerLeft = Bound(lower, _descriptor.Left);
        var upperBlock = CoordinateBlockStart(upperLeft, width);
        var lowerBlock = CoordinateBlockStart(lowerLeft, width);
        var upperEndpoint = VisibleCoordinateEndpoint(upper);
        var lowerEndpoint = VisibleCoordinateEndpoint(lower);
        Expression interior;
        Expression upperFringe;
        Expression lowerFringe;

        if (_descriptor.Scope is { } scope)
        {
            var scopeName = Expression.Constant(scope.Name);
            interior = NestedSetQueryExpressions.Inline(
                s_scopedInteriorFailureRights,
                upperEndpoint,
                failures.Expression,
                width,
                upperBlock,
                lowerBlock,
                scopeName,
                treeName,
                leftName,
                rightName);

            upperFringe = NestedSetQueryExpressions.Inline(
                s_scopedFringeFailureRights,
                upperEndpoint,
                failures.Expression,
                width,
                upperLeft,
                lowerLeft,
                scopeName,
                treeName,
                leftName,
                rightName);

            lowerFringe = NestedSetQueryExpressions.Inline(
                s_scopedFringeFailureRights,
                lowerEndpoint,
                failures.Expression,
                width,
                upperLeft,
                lowerLeft,
                scopeName,
                treeName,
                leftName,
                rightName);
        }
        else
        {
            interior = NestedSetQueryExpressions.Inline(
                s_unscopedInteriorFailureRights,
                upperEndpoint,
                failures.Expression,
                width,
                upperBlock,
                lowerBlock,
                treeName,
                leftName,
                rightName);

            upperFringe = NestedSetQueryExpressions.Inline(
                s_unscopedFringeFailureRights,
                upperEndpoint,
                failures.Expression,
                width,
                upperLeft,
                lowerLeft,
                treeName,
                leftName,
                rightName);

            lowerFringe = NestedSetQueryExpressions.Inline(
                s_unscopedFringeFailureRights,
                lowerEndpoint,
                failures.Expression,
                width,
                upperLeft,
                lowerLeft,
                treeName,
                leftName,
                rightName);
        }

        // WHY: In a valid nested set, a failure with upper.Left < Left <= lower.Left lies on this path
        // exactly when its Right >= lower.Right. MAX Right is therefore a complete existence proof.
        // Full interior blocks need only one summary each; both boundary blocks retain exact Left tests.
        // Same-block fringes may overlap harmlessly because only existence, not a count, is observed.
        var lowerRight = Expression.Convert(Bound(lower, _descriptor.Right), typeof(long?));
        var interiorViolation = Expression.GreaterThanOrEqual(
            Expression.Call(s_maximumNullableCoordinate, interior),
            lowerRight);

        var upperViolation = Expression.GreaterThanOrEqual(
            Expression.Call(s_maximumNullableCoordinate, upperFringe),
            lowerRight);

        var lowerViolation = Expression.GreaterThanOrEqual(
            Expression.Call(s_maximumNullableCoordinate, lowerFringe),
            lowerRight);

        // WHY: Three independent grouped relations cost a constant number of visible-node passes,
        // then O(sqrt(maxVisibleRight)) summaries/points per path on dense coordinates. One compact
        // correlated relation rematerialized failures per path in native plans. Nullable MAX preserves
        // an empty relation as no violation; no caller occurrence or payload row is deduplicated.
        return Expression.OrElse(interiorViolation, Expression.OrElse(upperViolation, lowerViolation));
    }
}
