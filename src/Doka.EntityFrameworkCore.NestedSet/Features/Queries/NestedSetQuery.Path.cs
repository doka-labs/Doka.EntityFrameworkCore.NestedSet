namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    private static readonly MethodInfo s_countPathChildren = MethodOf(
        (Expression<Func<IQueryable<TEntity>, long>>)(nodes => nodes.LongCount(node => true)));

    /// <summary>Filters rows by a complete visible path and the caller's direct-edge condition.</summary>
    internal IQueryable<TRow> WherePathEdges<TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> anchor,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, TEntity, bool>> edgeAllowed,
        bool normalized = false
    )
    {
        RequireSource(source);
        ArgumentNullException.ThrowIfNull(edgeAllowed);
        var sourceBindingMethod = SourceBindingMethod();
        var orderedJoinMethod = OrderedJoinMethod();
        bool? sourceAllowsVisibility = null;
        var anchorIdentity = NestedSetQueryEndpoint.Require(
            source,
            anchor,
            _entityType,
            _nodes.Expression,
            nameof(anchor),
            ref sourceAllowsVisibility,
            sourceBindingMethod,
            orderedJoinMethod);

        var nodeIdentity = NestedSetQueryEndpoint.Require(
            source,
            node,
            _entityType,
            _nodes.Expression,
            nameof(node),
            ref sourceAllowsVisibility,
            sourceBindingMethod,
            orderedJoinMethod);

        if (!normalized
            && NestedSetDeferredQuery.ContainsMarker(source.Expression))
        {
            // WHY: Validate endpoints immediately, then wait for EF to expand captured source queries.
            // Only the existing normalized proof can decide whether membership is actually redundant.
            return NestedSetDeferredQuery.CreatePath(
                _nodes,
                _unboundNodes,
                source,
                anchor,
                node,
                edgeAllowed,
                _entityType.Name,
                _treeBound);
        }

        var row = Expression.Parameter(typeof(TRow), "row");
        var selectedAnchor = NestedSetQueryExpressions.Inline(anchor, row);
        var selectedNode = NestedSetQueryExpressions.Inline(node, row);
        var upper = Expression.Parameter(typeof(TEntity), "anchor");
        var lower = Expression.Parameter(typeof(TEntity), "node");
        Expression? endpointsVisible = null;

        if (!anchorIdentity.IsVisible)
        {
            endpointsVisible = Any(_nodes, upper, MatchEndpoint(upper, selectedAnchor));
        }

        if (!nodeIdentity.IsVisible)
        {
            var nodeVisible = Any(_nodes, lower, MatchEndpoint(lower, selectedNode));
            endpointsVisible = endpointsVisible is null
                ? nodeVisible
                : Expression.AndAlso(endpointsVisible, nodeVisible);
        }

        // WHY: Keep endpoint membership in a scalar CASE rather than nesting the path under EXISTS.
        // MySQL otherwise merges each leg's endpoint semijoins into an exponentially costly join search.
        // An alias already resolved through this exact bound root needs no second membership lookup.
        Expression count = Expression.Convert(
            PathEdgeEvidence(selectedAnchor, selectedNode, row, edgeAllowed),
            typeof(long?));

        if (endpointsVisible is not null)
        {
            count = Expression.Condition(endpointsVisible, count, Expression.Constant(null, typeof(long?)));
        }

        var distance = Expression.Subtract(
            Expression.Convert(Depth(selectedNode), typeof(long)),
            Expression.Convert(Depth(selectedAnchor), typeof(long)));

        // WHY: In a tree-bound query, successful endpoint membership already proves their shared
        // scope and tree. Repeating that equality outside CASE would reintroduce the same constant-key
        // hash join avoided by the pair projection. Forest paths retain the explicit identity check.
        var containment = _treeBound
            ? ContainedBounds(selectedAnchor, selectedNode, includeSelf: true)
            : Contains(selectedAnchor, selectedNode, includeSelf: true);

        var condition = Expression.AndAlso(
            containment,
            Expression.Equal(count, Expression.Convert(distance, typeof(long?))));

        // WHY: The source pair already exposes its indexed coordinate range. Keeping this complete
        // path proof scalar prevents PostgreSQL from multiplying the same variable-range estimates again
        // and choosing a quadratic payload join. Containment still rejects unrelated supplied endpoints.
        return source.Where(Expression.Lambda<Func<TRow, bool>>(CorrelatedCondition(condition), row));
    }

    /// <summary>Proves visible direct-edge completeness without materializing hierarchy nodes.</summary>
    private ConditionalExpression PathEdgeEvidence(
        Expression upper,
        Expression lower,
        ParameterExpression row,
        LambdaExpression? edgeAllowed
    )
    {
        // WHY: Both endpoints already resolve to visible mapped rows in this SQL statement. Rejoining
        // those same rows for a short path repeats large query roots during EF parameter extraction.
        // One edge needs only their native FK equality; two edges need one unknown visible middle row.
        var direct = DirectPathEdge(upper, lower, row, edgeAllowed);
        var middle = Expression.Parameter(typeof(TEntity), "middle");
        var twoEdges = Any(
            _nodes,
            middle,
            Expression.AndAlso(
                DirectPathEdge(upper, middle, row, edgeAllowed),
                Expression.AndAlso(
                    DirectPathEdge(middle, lower, row, edgeAllowed),
                    Expression.AndAlso(
                        Contains(upper, middle, includeSelf: true),
                        Contains(middle, lower, includeSelf: true)))));

        var distance = Expression.Subtract(
            Expression.Convert(Depth(lower), typeof(long)),
            Expression.Convert(Depth(upper), typeof(long)));

        Expression longPath;

        if (_capabilities.UsesQueryFailureRelation
            && (edgeAllowed is null
                || !NestedSetQueryExpressions.References(edgeAllowed.Body, edgeAllowed.Parameters[0])))
        {
            _edges ??= NestedSetParentExpressions<TEntity, NestedSetQueryEdge<TEntity>>.Join(
                _nodes,
                _descriptor.NodeKey.Resolve(_entityType),
                _descriptor.Parent.Resolve(_entityType),
                _keyCollation,
                s_edgeProjection);

            // WHY: On valid bounds/depth, a path is complete exactly when none of its children lacks
            // a visible allowed direct parent. Row-independent failures permit a shared bucket proof;
            // row-aware rules below must still retain their own exact joined count.
            longPath = Expression.Condition(
                InvalidPathChildren(upper, lower, edgeAllowed, _edges),
                s_invalidPath,
                distance);
        }
        else
        {
            var parent = Expression.Parameter(typeof(TEntity), "parent");
            var child = Expression.Parameter(typeof(TEntity), "child");
            var allowedParent = Expression.AndAlso(
                Contains(upper, parent, includeSelf: true),
                Expression.AndAlso(
                    Contains(parent, lower, includeSelf: true),
                    DirectPathEdge(parent, child, row, edgeAllowed)));

            Expression allowed = Expression.AndAlso(
                Contains(upper, child, includeSelf: false),
                Expression.AndAlso(
                    Contains(child, lower, includeSelf: true),
                    CorrelatedCondition(Any(_nodes, parent, allowedParent))));

            // WHY: The mapped parent key is unique, so one visible allowed parent contributes exactly
            // one edge per child. A scalar parent probe retains native key/collation/depth checks and
            // exposes the unique lookup instead of a free parent/child range join; actual plans qualify its work.
            // Row-aware rules still retain their exact per-row count; no caller value is hoisted.
            longPath = Expression.Call(
                s_countPathChildren,
                _nodes.Expression,
                Expression.Quote(Expression.Lambda<Func<TEntity, bool>>(allowed, child)));
        }

        // WHY: Wide and depth-two reports need only identity or one/two indexed direct-edge lookups.
        // Interval counting for each fact made valid broad profiles quadratic in the stored population.
        // Longer paths retain the same visible-edge proof, including hidden and corrupted parent rejection.
        return Expression.Condition(
            Expression.Equal(distance, s_zeroDistance),
            Expression.Condition(Expression.Equal(upper, lower), s_zeroDistance, s_invalidPath),
            Expression.Condition(
                Expression.Equal(distance, s_oneEdge),
                Expression.Condition(direct, s_oneEdge, s_zeroDistance),
                Expression.Condition(
                    Expression.Equal(distance, s_twoEdges),
                    Expression.Condition(twoEdges, s_twoEdges, s_zeroDistance),
                    longPath)));
    }

    /// <summary>Checks one direct edge whose endpoint aliases are already proven visible.</summary>
    private Expression DirectPathEdge(
        Expression parent,
        Expression child,
        ParameterExpression row,
        LambdaExpression? edgeAllowed
    )
    {
        var identity = NestedSetParentExpressions<TEntity, NestedSetQueryEdge<TEntity>>.Matches(
            parent,
            child,
            _descriptor.NodeKey.Resolve(_entityType),
            _descriptor.Parent.Resolve(_entityType),
            _keyCollation);

        Expression allowed = Expression.AndAlso(
            identity,
            Expression.Equal(
                Expression.Convert(Depth(child), typeof(long)),
                Expression.Add(Expression.Convert(Depth(parent), typeof(long)), s_oneEdge)));

        if (!_treeBound)
        {
            // WHY: Visible forest aliases may still belong to different trees, even with aligned bounds
            // and a corrupt cross-tree FK. The middle row must stay in the endpoint tree on both edges.
            allowed = Expression.AndAlso(allowed, RoleEqual(parent, child, _descriptor.TreeId));
        }

        if (edgeAllowed is not null)
        {
            allowed = Expression.AndAlso(allowed, NestedSetQueryExpressions.Inline(edgeAllowed, row, parent, child));
        }

        return allowed;
    }
}
