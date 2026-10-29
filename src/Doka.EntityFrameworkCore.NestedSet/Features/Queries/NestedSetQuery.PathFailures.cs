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

    private static readonly MethodInfo s_firstPathFailure = MethodOf(
        (Expression<Func<IQueryable<long?>, long?>>)(failures => failures.FirstOrDefault()));

    /// <summary>Rejects paths whose interior blocks or endpoint fringes contain an invalid direct child.</summary>
    private BinaryExpression InvalidPathChildren(
        Expression upper,
        Expression lower,
        LambdaExpression? edgeAllowed,
        IQueryable<NestedSetQueryEdge<TEntity>> edges
    )
    {
        var failures = InvalidPathChildSource(edgeAllowed, edges);

        if (_capabilities.PathFailureStrategy == NestedSetPathFailureStrategy.CompletedPoints)
        {
            return InvalidCompletedPathChildren(upper, lower, failures);
        }

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

    /// <summary>Selects visible children without a visible, structurally valid and allowed direct parent.</summary>
    private IQueryable<TEntity> InvalidPathChildSource(
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

        return _nodes.Where(
            Expression.Lambda<Func<TEntity, bool>>(Expression.Not(Any(edges, edge, valid)), invalid));
    }

    /// <summary>Fences invalid-child points before testing each path's exact coordinate window.</summary>
    private BinaryExpression InvalidCompletedPathChildren(
        Expression upper,
        Expression lower,
        IQueryable<TEntity> failures
    )
    {
        var child = Expression.Parameter(typeof(TEntity), "failure");
        var scope = _descriptor.Scope is { } role
            ? NestedSetExpressions.Property(child, role.Resolve(_entityType), typeof(object))
            : (Expression)Expression.Constant(null, typeof(object));

        var selector = Expression.Lambda<Func<TEntity, PathFailurePoint>>(
            Expression.MemberInit(Expression.New(typeof(PathFailurePoint)),
                Field(nameof(PathFailurePoint.Scope), scope),
                Field(nameof(PathFailurePoint.Tree),
                    NestedSetExpressions.Property(child, _descriptor.TreeId.Resolve(_entityType), typeof(object))),
                Field(nameof(PathFailurePoint.Left), Bound(child, _descriptor.Left)),
                Field(nameof(PathFailurePoint.Right), Bound(child, _descriptor.Right))), child);

        // WHY: SQL Server reran grouped failure summaries for every path row: the scalar MAX stayed
        // correlated, so neither its hash joins nor its population scans were reused. These points do not
        // reference outer path bounds; behind OFFSET 0 the measured plans completed them once.
        var points = failures.Select(selector).OrderBy(point => point.Left).Skip(0);

        var point = Expression.Parameter(typeof(PathFailurePoint), "point");
        Expression identity = Expression.Equal(Expression.Property(point, nameof(PathFailurePoint.Tree)),
            NestedSetExpressions.Property(upper, _descriptor.TreeId.Resolve(_entityType), typeof(object)));

        if (_descriptor.Scope is { } scopeRole)
        {
            identity = Expression.AndAlso(identity,
                Expression.Equal(Expression.Property(point, nameof(PathFailurePoint.Scope)),
                    NestedSetExpressions.Property(upper, scopeRole.Resolve(_entityType), typeof(object))));
        }

        // WHY: In a valid nested set, a failure with upper.Left < Left <= lower.Left lies on this path
        // exactly when its Right >= lower.Right. Existence is the complete proof; no caller row, payload
        // value or occurrence is deduplicated, and an empty point relation proves no violation.
        var left = Expression.Property(point, nameof(PathFailurePoint.Left));
        var onPath = Expression.AndAlso(identity,
            Expression.AndAlso(
                Expression.AndAlso(
                    Expression.GreaterThan(left, Bound(upper, _descriptor.Left)),
                    Expression.LessThanOrEqual(left, Bound(lower, _descriptor.Left))),
                Expression.GreaterThanOrEqual(
                    Expression.Property(point, nameof(PathFailurePoint.Right)), Bound(lower, _descriptor.Right))));

        // WHY: EXISTS let SQL Server turn this proof into an eager probe join beside nearest candidates,
        // rebuilding the completed points for every row. In the measured plans a scalar TOP(1) remained a
        // lazy correlated lookup that short paths skipped and long paths answered from the completed points.
        var found = Expression.Call(s_firstPathFailure,
            points.Where(Expression.Lambda<Func<PathFailurePoint, bool>>(onPath, point))
                .Select(failure => (long?)1L).Expression);

        return Expression.Equal(found, Expression.Constant(1L, typeof(long?)));

        static MemberAssignment Field(string member, Expression value)
            => Expression.Bind(typeof(PathFailurePoint).GetProperty(member)!, value);
    }

    /// <summary>Carries native identity and the exact interval of one invalid child across the fence.</summary>
    private sealed class PathFailurePoint
    {
        /// <summary>Gets the scope role only for a scoped model.</summary>
        public object? Scope { get; init; }

        /// <summary>Gets the native mapped tree identity.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the invalid child's left coordinate.</summary>
        public long Left { get; init; }

        /// <summary>Gets the invalid child's right coordinate.</summary>
        public long Right { get; init; }
    }
}
