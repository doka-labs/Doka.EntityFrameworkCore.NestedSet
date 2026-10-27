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
            return NestedSetDeferredQuery.CreateAny(
                _nodes,
                _unboundNodes,
                source,
                node,
                predicate,
                _entityType.Name,
                _treeBound);
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

        return source.Where(Expression.Lambda<Func<TRow, bool>>(CorrelatedCondition(condition), row));
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

        // WHY: Row-aware and query-bearing Doka inputs retain their original correlation; SQLite
        // retains its indexed candidate lookup. Only Doka has qualified shared coordinate groups.
        var candidates = Expression.Call(
            s_where,
            _nodes.Expression,
            Expression.Quote(
                Expression.Lambda<Func<TEntity, bool>>(
                    Expression.AndAlso(Contains(owner, lower, includeSelf: false), selected.Body),
                    owner)));

        // WHY: On a valid nested set, ancestor Left increases strictly with structural depth.
        // The existing tree/Left index can therefore select the same nearest owner without a Depth sort.
        var ordered = Expression.Call(
            s_orderByLeft,
            candidates,
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
            return NestedSetDeferredQuery.CreateNearest(
                _nodes,
                _unboundNodes,
                source,
                node,
                candidate,
                predicate,
                _entityType.Name,
                _treeBound);
        }

        var row = Expression.Parameter(typeof(TRow), "row");
        var selected = NestedSetQueryExpressions.Inline(node, row);
        var membership = Expression.Parameter(typeof(TEntity), "node");
        var lower = identity.IsVisible ? selected : membership;
        Expression strictAccepted = _capabilities.UsesProjectedQueryDecision
            ? ProjectedStrictNearestDecision(lower, row, candidate, predicate)
            : StrictNearestExists(lower, row, candidate, predicate);

        // WHY: Self is already the nearest visible candidate and its path has no edges. Resolve it
        // before the ancestor lookup, including rejection without fallback. The separate keyed owner
        // shape on other providers keeps PostgreSQL's qualified owner-key access available.
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

        return source.Where(Expression.Lambda<Func<TRow, bool>>(CorrelatedCondition(condition), row));
    }

    /// <summary>Retains the qualified scalar-key owner lookup on providers without the Doka decision shape.</summary>
    private ConditionalExpression StrictNearestExists(
        Expression lower,
        ParameterExpression row,
        LambdaExpression candidate,
        LambdaExpression predicate
    )
    {
        var owner = Expression.Parameter(typeof(TEntity), "owner");
        var possible = Expression.Parameter(typeof(TEntity), "possible");
        var candidates = Expression.Call(
            s_where,
            _nodes.Expression,
            Expression.Quote(
                Expression.Lambda<Func<TEntity, bool>>(
                    Expression.AndAlso(
                        Contains(possible, lower, includeSelf: false),
                        NestedSetQueryExpressions.Inline(candidate, row, possible)),
                    possible)));

        var ordered = Expression.Call(
            s_orderByDepth,
            candidates,
            Expression.Quote(Expression.Lambda<Func<TEntity, int>>(Depth(possible), possible)));

        var key = _descriptor.NodeKey.Resolve(_entityType);
        var keys = Expression.Call(
            s_selectKey,
            ordered,
            Expression.Quote(
                Expression.Lambda<Func<TEntity, object>>(
                    NestedSetExpressions.Property(possible, key, typeof(object)),
                    possible)));

        var nearestKey = Expression.Call(s_firstKey, keys);

        // WHY: Selecting a scalar node key exposes the scope/key unique index for one owner lookup.
        // Comparing a scalar MAX depth instead made MySQL rescan all candidates for every owner.
        // EF.Property<object> is a translation marker: unlike boxing a typed access, it adds no
        // index-defeating CAST on the owner column. An absent scalar yields NULL, never key 0.
        var selectedOwner = Expression.AndAlso(
            Contains(owner, lower, includeSelf: false),
            Expression.Equal(NestedSetExpressions.Property(owner, key, typeof(object)), nearestKey));

        var matches = Expression.AndAlso(selectedOwner, NestedSetQueryExpressions.Inline(predicate, row, owner));

        // WHY: SQL may reorder AND conditions. Keeping the scalar count in CASE's result prevents
        // premature path evaluation; a boolean CASE can be folded back into AND by SQL Server's
        // bit conversion. Actual provider plans qualify the cost rather than assuming short-circuiting.
        var visibleEdges = Expression.Condition(
            matches,
            Expression.Convert(PathEdgeEvidence(owner, lower, row, edgeAllowed: null), typeof(long?)),
            Expression.Constant(null, typeof(long?)));

        var distance = Expression.Subtract(
            Expression.Convert(Depth(lower), typeof(long)),
            Expression.Convert(Depth(owner), typeof(long)));

        // WHY: Keep key equality outside CASE as an index access predicate as well. Hiding it only
        // inside CASE made SQLite enumerate every owner and repeat the ordered scalar selection.
        var ownerPredicate = Expression.AndAlso(
            selectedOwner,
            Expression.Equal(visibleEdges, Expression.Convert(distance, typeof(long?))));

        return Expression.Condition(
            Any(_nodes, owner, ownerPredicate),
            Expression.Constant(1L),
            Expression.Constant(0L));
    }
}
