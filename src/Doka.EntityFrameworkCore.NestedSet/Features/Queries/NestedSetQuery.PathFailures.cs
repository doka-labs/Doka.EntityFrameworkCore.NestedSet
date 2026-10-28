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
        var failures = InvalidPathChildSource(edgeAllowed, edges);

        if (_capabilities.PathFailureStrategy == NestedSetPathFailureStrategy.RelationalWidth)
        {
            return InvalidRelationalPathChildren(upper, lower, failures);
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

    /// <summary>Checks local grouped failure coordinates without scalar subqueries inside grouping or aggregates.</summary>
    private BinaryExpression InvalidRelationalPathChildren(
        Expression upper,
        Expression lower,
        IQueryable<TEntity> failures
    )
    {
        var child = Expression.Parameter(typeof(TEntity), "failure");
        var scope = _descriptor.Scope is { } role
            ? NestedSetExpressions.Property(child, role.Resolve(_entityType), typeof(object))
            : (Expression)Expression.Constant(null, typeof(object));

        var selector = Expression.Lambda<Func<TEntity, PathFailureCoordinate>>(
            Expression.MemberInit(Expression.New(typeof(PathFailureCoordinate)),
                Field(nameof(PathFailureCoordinate.Scope), scope),
                Field(nameof(PathFailureCoordinate.Tree),
                    NestedSetExpressions.Property(child, _descriptor.TreeId.Resolve(_entityType), typeof(object))),
                Field(nameof(PathFailureCoordinate.Left), Bound(child, _descriptor.Left)),
                Field(nameof(PathFailureCoordinate.Right), Bound(child, _descriptor.Right))), child);

        var widths = Expression.Lambda<Func<PathFailureCoordinate, IEnumerable<long>>>(
            VisibleCoordinateBlockWidths().Expression, Expression.Parameter(typeof(PathFailureCoordinate), "point"));

        var points = failures.Select(selector).SelectMany(widths, (point, width) => new PathFailureCoordinate
        {
            Scope = point.Scope, Tree = point.Tree, Left = point.Left, Right = point.Right,
            Width = width, Block = point.Left - point.Left % width,
        });

        // WHY: Both aggregates consume only local columns. Their width is a real relational column,
        // never MAX/SQRT subqueries repeated inside GROUP BY. Scope is omitted entirely when absent.
        var interior = _descriptor.Scope is null
            ? points.GroupBy(point => new { point.Tree, point.Width, point.Block })
                .Select(group => new PathFailureCoordinate
                {
                    Tree = group.Key.Tree, Width = group.Key.Width, Block = group.Key.Block,
                    Right = group.Max(point => point.Right),
                })
            : points.GroupBy(point => new { point.Scope, point.Tree, point.Width, point.Block })
                .Select(group => new PathFailureCoordinate
                {
                    Scope = group.Key.Scope, Tree = group.Key.Tree, Width = group.Key.Width, Block = group.Key.Block,
                    Right = group.Max(point => point.Right),
                });

        var fringes = _descriptor.Scope is null
            ? points.GroupBy(point => new { point.Tree, point.Width, point.Block, point.Left })
                .Select(group => new PathFailureCoordinate
                {
                    Tree = group.Key.Tree, Width = group.Key.Width, Block = group.Key.Block, Left = group.Key.Left,
                    Right = group.Max(point => point.Right),
                })
            : points.GroupBy(point => new { point.Scope, point.Tree, point.Width, point.Block, point.Left })
                .Select(group => new PathFailureCoordinate
                {
                    Scope = group.Key.Scope, Tree = group.Key.Tree, Width = group.Key.Width,
                    Block = group.Key.Block, Left = group.Key.Left, Right = group.Max(point => point.Right),
                });

        var partition = Expression.Parameter(typeof(PathFailureCoordinate), "partition");
        var tree = Expression.Equal(Expression.Property(partition, nameof(PathFailureCoordinate.Tree)),
            NestedSetExpressions.Property(upper, _descriptor.TreeId.Resolve(_entityType), typeof(object)));

        Expression identity = _descriptor.Scope is { } scopeRole
            ? Expression.AndAlso(tree,
                Expression.Equal(Expression.Property(partition, nameof(PathFailureCoordinate.Scope)),
                    NestedSetExpressions.Property(upper, scopeRole.Resolve(_entityType), typeof(object))))
            : tree;

        var widthColumn = Expression.Property(partition, nameof(PathFailureCoordinate.Width));
        var block = Expression.Property(partition, nameof(PathFailureCoordinate.Block));
        var left = Expression.Property(partition, nameof(PathFailureCoordinate.Left));
        var right = Expression.Property(partition, nameof(PathFailureCoordinate.Right));
        var upperLeft = Bound(upper, _descriptor.Left);
        var lowerLeft = Bound(lower, _descriptor.Left);
        var upperBlock = CoordinateBlockStart(upperLeft, widthColumn);
        var lowerBlock = CoordinateBlockStart(lowerLeft, widthColumn);
        var reachesLower = Expression.GreaterThanOrEqual(right, Bound(lower, _descriptor.Right));
        var interiorMatch = Expression.AndAlso(identity, Expression.AndAlso(reachesLower,
            Expression.AndAlso(Expression.GreaterThan(block, upperBlock), Expression.LessThan(block, lowerBlock))));

        var fringeMatch = Expression.AndAlso(identity, Expression.AndAlso(reachesLower,
            Expression.AndAlso(
                Expression.OrElse(Expression.Equal(block, upperBlock), Expression.Equal(block, lowerBlock)),
                Expression.AndAlso(Expression.GreaterThan(left, upperLeft), Expression.LessThanOrEqual(left, lowerLeft)))));

        // WHY: EXISTS filters completed local summaries. MAX(CASE ... outer path columns ... Right)
        // is invalid on SQL Server; separate WHERE predicates preserve the same existence proof.
        // Combining both fringes is safe even in one block because caller occurrences are never grouped.
        return Expression.OrElse(Any(interior, partition, interiorMatch), Any(fringes, partition, fringeMatch));

        static MemberAssignment Field(string member, Expression value)
            => Expression.Bind(typeof(PathFailureCoordinate).GetProperty(member)!, value);
    }

    /// <summary>Carries native identity and local summary columns across one relational width boundary.</summary>
    private sealed class PathFailureCoordinate
    {
        /// <summary>Gets the scope role only for a scoped model.</summary>
        public object? Scope { get; init; }

        /// <summary>Gets the native mapped tree identity.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the current visible population's adaptive partition width.</summary>
        public long Width { get; init; }

        /// <summary>Gets the overflow-safe coordinate block start.</summary>
        public long Block { get; init; }

        /// <summary>Gets the exact left coordinate retained by fringe summaries.</summary>
        public long Left { get; init; }

        /// <summary>Gets the greatest failed right coordinate within the local summary.</summary>
        public long Right { get; init; }
    }
}
