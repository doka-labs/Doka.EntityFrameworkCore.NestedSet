namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Filters rows using any visible matching ancestor without asserting a complete visible path.</summary>
    internal IQueryable<TRow> WhereAnyAncestorOrSelf<TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, bool>> predicate,
        bool normalized = false
    )
    {
        RequireSource(source);
        ArgumentNullException.ThrowIfNull(predicate);
        var identity = NestedSetQueryEndpoint.Require(
            source,
            node,
            _entityType,
            _nodes.Expression,
            nameof(node),
            SourceBindingMethod(),
            OrderedJoinMethod());

        if (!normalized
            && NestedSetDeferredQuery.ContainsMarker(source.Expression))
        {
            return Composed(NestedSetDeferredQuery.CreateAny(
                _nodes,
                _unboundNodes,
                source,
                node,
                predicate,
                _entityType.Name,
                _treeBound));
        }

        var row = Expression.Parameter(typeof(TRow), "row");
        var selected = NestedSetQueryExpressions.Inline(node, row);
        var membership = Expression.Parameter(typeof(TEntity), "node");
        var lower = identity.IsVisible ? selected : membership;

        var owner = Expression.Parameter(typeof(TEntity), "owner");
        var ownerPredicate = Expression.AndAlso(
            Contains(owner, lower, includeSelf: false),
            NestedSetQueryExpressions.Inline(predicate, row, owner));

        // WHY: A matching visible self already proves existence. Keeping the strict-ancestor search
        // in a scalar CASE avoids a broad owner semijoin that made PostgreSQL repeatedly scan the
        // payload table. This preserves Any's ability to match above a hidden intermediate node.
        var accepted = Expression.Condition(
            NestedSetQueryExpressions.Inline(predicate, row, lower),
            Expression.Constant(1L),
            Expression.Condition(Any(_nodes, owner, ownerPredicate), Expression.Constant(1L), Expression.Constant(0L)));

        // WHY: A proven alias already belongs to this exact visible scope/tree root. Uncertain external
        // aliases still resolve through complete membership before any ancestor or self decision qualifies.
        Expression condition = Expression.Equal(accepted, Expression.Constant(1L));

        if (!identity.IsVisible)
        {
            condition = Any(_nodes, membership, Expression.AndAlso(MatchEndpoint(membership, selected), condition));
        }

        return Composed(source.Where(Expression.Lambda<Func<TRow, bool>>(CorrelatedCondition(condition), row)));
    }

    /// <summary>Selects a scalar nearest-owner decision without repeating the nearest-key relation.</summary>
    private MethodCallExpression ProjectedStrictNearestDecision(
        Expression lower,
        ParameterExpression row,
        LambdaExpression candidate,
        LambdaExpression predicate
    )
    {
        var owner = Expression.Parameter(typeof(TEntity), "owner");
        var selected = Expression.Lambda<Func<TEntity, bool>>(
            NestedSetQueryExpressions.Inline(candidate, row, owner),
            owner);

        if (_capabilities.UsesPartitionedNearestDecision
            && !NestedSetQueryExpressions.References(candidate.Body, candidate.Parameters[0])
            && !NestedSetQuerySelection.UsesQueryOperators(selected))
        {
            return PartitionedNearestDecision(lower, row, selected, predicate);
        }

        // WHY: Row-aware and query-bearing inputs retain their original correlation. Only Doka
        // has qualified shared coordinate groups; the other adapters keep one scalar owner lookup.
        var candidates = Expression.Call(
            s_where,
            _nodes.Expression,
            Expression.Quote(
                Expression.Lambda<Func<TEntity, bool>>(
                    Expression.AndAlso(Contains(owner, lower, includeSelf: false), selected.Body),
                    owner)));

        // WHY: PostgreSQL retains the Depth ordering of its measured broad-payload correction.
        // Projecting the decision once removes two scalar key sorts and their owner rejoin whose
        // inactive subplans distorted broad join estimates. On valid ancestor chains, descending Left
        // is equivalent; the other adapters use that existing coordinate order without a separate depth sort.
        var ordered = _capabilities.NearestQueryStrategy == NestedSetNearestQueryStrategy.ProjectedDepth
            ? Expression.Call(s_orderByDepth, candidates,
                Expression.Quote(Expression.Lambda<Func<TEntity, int>>(Depth(owner), owner)))
            : Expression.Call(s_orderByLeft, candidates,
                Expression.Quote(Expression.Lambda<Func<TEntity, long>>(Bound(owner, _descriptor.Left), owner)));

        return SelectNearestOwnerDecision(ordered, owner, lower, row, predicate);
    }

    /// <summary>Applies value and complete-path decisions only after exact structural owner selection.</summary>
    private MethodCallExpression SelectNearestOwnerDecision(
        Expression candidates,
        ParameterExpression owner,
        Expression lower,
        ParameterExpression row,
        LambdaExpression predicate
    )
    {
        var distance = Expression.Subtract(
            Expression.Convert(Depth(lower), typeof(long)),
            Expression.Convert(Depth(owner), typeof(long)));

        var path = Expression.Equal(PathEdgeEvidence(owner, lower, row, edgeAllowed: null), distance);
        var decision = Expression.Condition(
            NestedSetQueryExpressions.Inline(predicate, row, owner),
            Expression.Condition(path, s_oneEdge, s_zeroDistance),
            s_zeroDistance);

        // WHY: Candidate selection stays in WHERE and value/path stay in SELECT before FirstOrDefault.
        // A rejected nearest candidate yields zero without searching farther; an absent candidate also
        // yields zero. Native plans must still prove the actual projection and index work around LIMIT.
        var decisions = Expression.Call(
            s_selectDecision,
            candidates,
            Expression.Quote(Expression.Lambda<Func<TEntity, long>>(decision, owner)));

        return Expression.Call(s_firstDecision, decisions);
    }

    /// <summary>Selects the nearest candidate before applying its value and complete-path conditions.</summary>
    internal IQueryable<TRow> WhereNearestAncestorOrSelf<TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, bool>> candidate,
        Expression<Func<TRow, TEntity, bool>> predicate,
        bool normalized = false
    )
    {
        RequireSource(source);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(predicate);
        var identity = NestedSetQueryEndpoint.Require(
            source,
            node,
            _entityType,
            _nodes.Expression,
            nameof(node),
            SourceBindingMethod(),
            OrderedJoinMethod());

        if (!normalized
            && NestedSetDeferredQuery.ContainsMarker(source.Expression))
        {
            return Composed(NestedSetDeferredQuery.CreateNearest(
                _nodes,
                _unboundNodes,
                source,
                node,
                candidate,
                predicate,
                _entityType.Name,
                _treeBound));
        }

        var row = Expression.Parameter(typeof(TRow), "row");
        var selected = NestedSetQueryExpressions.Inline(node, row);
        var membership = Expression.Parameter(typeof(TEntity), "node");
        var lower = identity.IsVisible ? selected : membership;
        var strictAccepted = ProjectedStrictNearestDecision(lower, row, candidate, predicate);

        // WHY: Self is already the nearest visible candidate and its path has no edges. Resolve it
        // before the ancestor lookup, including rejection without fallback. Each adapter retains
        // its own ordering while sharing the same selected-owner value and complete-path decision.
        var accepted = Expression.Condition(
            NestedSetQueryExpressions.Inline(candidate, row, lower),
            Expression.Condition(
                NestedSetQueryExpressions.Inline(predicate, row, lower),
                Expression.Constant(1L),
                Expression.Constant(0L)),
            strictAccepted);

        // WHY: Only exact bound-root provenance permits the selected alias to stand in for the visible
        // member. Raw sets, another facade and query-wide filter changes retain the membership barrier.
        Expression condition = Expression.Equal(accepted, Expression.Constant(1L));

        if (!identity.IsVisible)
        {
            condition = Any(_nodes, membership, Expression.AndAlso(MatchEndpoint(membership, selected), condition));
        }

        return Composed(source.Where(Expression.Lambda<Func<TRow, bool>>(CorrelatedCondition(condition), row)));
    }
}
