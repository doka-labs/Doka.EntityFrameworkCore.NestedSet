namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    // WHY: Native scoped/unscoped joins retain mapped identity and omit an absent scope entirely.
    // One fringe template serves either coordinate role; its grouped points expose a scope/tree/block
    // access key without carrying entities or keeping any context, model or request in static state.
    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string, string,
            string, IQueryable<long?>>> s_scopedCandidateFringeLefts = (
            endpoints,
            candidates,
            width,
            lowerLeft,
            lowerRight,
            partition,
            scope,
            tree,
            left,
            right
        ) => endpoints
            .Join(
                candidates
                    .GroupBy(owner => new
                    {
                        Scope = EF.Property<object>(owner, scope),
                        Tree = EF.Property<object>(owner, tree),
                        Block = EF.Property<long>(owner, partition) - EF.Property<long>(owner, partition) % width,
                        CandidateLeft = EF.Property<long>(owner, left),
                        CandidateRight = EF.Property<long>(owner, right),
                    })
                    .Select(group => group.Key),
                endpoint => new
                {
                    Scope = EF.Property<object>(endpoint, scope),
                    Tree = EF.Property<object>(endpoint, tree),
                    Block = EF.Property<long>(endpoint, partition) - EF.Property<long>(endpoint, partition) % width,
                },
                point => new
                {
                    point.Scope,
                    point.Tree,
                    point.Block,
                },
                (endpoint, point) => point)
            .Select(point => point.CandidateLeft < lowerLeft && point.CandidateRight > lowerRight
                ? (long?)point.CandidateLeft
                : null);

    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string, string,
            IQueryable<long?>>> s_unscopedCandidateFringeLefts = (
            endpoints,
            candidates,
            width,
            lowerLeft,
            lowerRight,
            partition,
            tree,
            left,
            right
        ) => endpoints
            .Join(
                candidates
                    .GroupBy(owner => new
                    {
                        Tree = EF.Property<object>(owner, tree),
                        Block = EF.Property<long>(owner, partition) - EF.Property<long>(owner, partition) % width,
                        CandidateLeft = EF.Property<long>(owner, left),
                        CandidateRight = EF.Property<long>(owner, right),
                    })
                    .Select(group => group.Key),
                endpoint => new
                {
                    Tree = EF.Property<object>(endpoint, tree),
                    Block = EF.Property<long>(endpoint, partition) - EF.Property<long>(endpoint, partition) % width,
                },
                point => new
                {
                    point.Tree,
                    point.Block,
                },
                (endpoint, point) => point)
            .Select(point => point.CandidateLeft < lowerLeft && point.CandidateRight > lowerRight
                ? (long?)point.CandidateLeft
                : null);

    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string, string,
            IQueryable<long?>>> s_scopedInteriorCandidateLefts = (
            endpoints,
            candidates,
            width,
            lowerLeftBlock,
            lowerRightBlock,
            scope,
            tree,
            left,
            right
        ) => endpoints
            .Join(
                candidates
                    .GroupBy(owner => new
                    {
                        Scope = EF.Property<object>(owner, scope),
                        Tree = EF.Property<object>(owner, tree),
                        LeftBlock = EF.Property<long>(owner, left) - EF.Property<long>(owner, left) % width,
                        RightBlock = EF.Property<long>(owner, right) - EF.Property<long>(owner, right) % width,
                    })
                    .Select(group => new
                    {
                        group.Key.Scope,
                        group.Key.Tree,
                        group.Key.LeftBlock,
                        group.Key.RightBlock,
                        CandidateLeft = group.Max(owner => EF.Property<long>(owner, left)),
                    }),
                endpoint => new
                {
                    Scope = EF.Property<object>(endpoint, scope),
                    Tree = EF.Property<object>(endpoint, tree),
                },
                cell => new
                {
                    cell.Scope,
                    cell.Tree,
                },
                (endpoint, cell) => cell)
            .Select(cell => cell.LeftBlock < lowerLeftBlock && cell.RightBlock > lowerRightBlock
                ? (long?)cell.CandidateLeft
                : null);

    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string,
            IQueryable<long?>>> s_unscopedInteriorCandidateLefts = (
            endpoints,
            candidates,
            width,
            lowerLeftBlock,
            lowerRightBlock,
            tree,
            left,
            right
        ) => endpoints
            .Join(
                candidates
                    .GroupBy(owner => new
                    {
                        Tree = EF.Property<object>(owner, tree),
                        LeftBlock = EF.Property<long>(owner, left) - EF.Property<long>(owner, left) % width,
                        RightBlock = EF.Property<long>(owner, right) - EF.Property<long>(owner, right) % width,
                    })
                    .Select(group => new
                    {
                        group.Key.Tree,
                        group.Key.LeftBlock,
                        group.Key.RightBlock,
                        CandidateLeft = group.Max(owner => EF.Property<long>(owner, left)),
                    }),
                endpoint => EF.Property<object>(endpoint, tree),
                cell => cell.Tree,
                (endpoint, cell) => cell)
            .Select(cell => cell.LeftBlock < lowerLeftBlock && cell.RightBlock > lowerRightBlock
                ? (long?)cell.CandidateLeft
                : null);

    /// <summary>Selects an independent strict candidate through exact coordinate fringes and interior cells.</summary>
    private MethodCallExpression PartitionedNearestDecision(
        Expression lower,
        ParameterExpression row,
        Expression<Func<TEntity, bool>> candidate,
        LambdaExpression predicate
    )
    {
        var candidates = _nodes.Where(candidate).Expression;
        var endpoint = VisibleCoordinateEndpoint(lower);
        var width = VisibleCoordinateBlockWidth();
        var lowerLeft = Bound(lower, _descriptor.Left);
        var lowerRight = Bound(lower, _descriptor.Right);
        var lowerLeftBlock = CoordinateBlockStart(lowerLeft, width);
        var lowerRightBlock = CoordinateBlockStart(lowerRight, width);
        var treeName = Expression.Constant(_descriptor.TreeId.Name);
        var leftName = Expression.Constant(_descriptor.Left.Name);
        var rightName = Expression.Constant(_descriptor.Right.Name);
        Expression leftFringe;
        Expression rightFringe;
        Expression interior;

        if (_descriptor.Scope is { } scope)
        {
            var scopeName = Expression.Constant(scope.Name);
            leftFringe = NestedSetQueryExpressions.Inline(
                s_scopedCandidateFringeLefts,
                endpoint,
                candidates,
                width,
                lowerLeft,
                lowerRight,
                leftName,
                scopeName,
                treeName,
                leftName,
                rightName);

            rightFringe = NestedSetQueryExpressions.Inline(
                s_scopedCandidateFringeLefts,
                endpoint,
                candidates,
                width,
                lowerLeft,
                lowerRight,
                rightName,
                scopeName,
                treeName,
                leftName,
                rightName);

            interior = NestedSetQueryExpressions.Inline(
                s_scopedInteriorCandidateLefts,
                endpoint,
                candidates,
                width,
                lowerLeftBlock,
                lowerRightBlock,
                scopeName,
                treeName,
                leftName,
                rightName);
        }
        else
        {
            leftFringe = NestedSetQueryExpressions.Inline(
                s_unscopedCandidateFringeLefts,
                endpoint,
                candidates,
                width,
                lowerLeft,
                lowerRight,
                leftName,
                treeName,
                leftName,
                rightName);

            rightFringe = NestedSetQueryExpressions.Inline(
                s_unscopedCandidateFringeLefts,
                endpoint,
                candidates,
                width,
                lowerLeft,
                lowerRight,
                rightName,
                treeName,
                leftName,
                rightName);

            interior = NestedSetQueryExpressions.Inline(
                s_unscopedInteriorCandidateLefts,
                endpoint,
                candidates,
                width,
                lowerLeftBlock,
                lowerRightBlock,
                treeName,
                leftName,
                rightName);
        }

        // WHY: Every strict ancestor is in the lower's left block, its right block, or an earlier-left
        // and later-right block pair. All points in an interior cell contain lower, so MAX Left is exact.
        // A left-fringe ancestor has greater Left than every ancestor in an earlier left block. Among
        // the rest, a right-fringe ancestor has smaller Right than every interior ancestor; laminar
        // containment makes it deeper, with greater Left. Nullable COALESCE therefore preserves nearest
        // order without comparing/repeating aggregates or reserving a key or coordinate as absence.
        var selectedLeft = Expression.Coalesce(
            Expression.Call(s_maximumNullableCoordinate, leftFringe),
            Expression.Coalesce(
                Expression.Call(s_maximumNullableCoordinate, rightFringe),
                Expression.Call(s_maximumNullableCoordinate, interior)));

        // WHY: Valid nested-set intervals are laminar. With K coordinate blocks per tree, their
        // noncrossing pairs have at most 3K - 3 cells for K >= 2: 2K - 3 outerplanar edges plus K loops.
        // Three independent grouped relations trade constant population preprocessing for local fringe
        // and compact interior work. This is a cardinality argument, not an optimizer execution promise;
        // native plans must qualify actual work. No copied caller source or correlated recovery is needed.
        var owner = Expression.Parameter(typeof(TEntity), "owner");
        var coordinate = Expression.Equal(
            Expression.Convert(Bound(owner, _descriptor.Left), typeof(long?)),
            selectedLeft);

        var selectedOwner = _treeBound
            ? coordinate
            : Expression.AndAlso(RoleEqual(owner, lower, _descriptor.TreeId), coordinate);

        var owners = Expression.Call(
            s_where,
            _nodes.Expression,
            Expression.Quote(Expression.Lambda<Func<TEntity, bool>>(selectedOwner, owner)));

        // WHY: Only eligibility and complete mapped identity choose the coordinate. The selected actual
        // owner then receives the original value and complete-path decisions. Rejection cannot choose
        // a farther candidate, and the original source occurrence bag remains the result being filtered.
        return SelectNearestOwnerDecision(owners, owner, lower, row, predicate);
    }
}
