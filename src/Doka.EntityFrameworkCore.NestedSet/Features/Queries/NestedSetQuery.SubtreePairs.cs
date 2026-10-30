namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Expands selected ancestors from each actual related alias without a global pair UNION.</summary>
    private IQueryable<TResult> SelectRelatedFirstSubtreePairs<TResult>(
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    )
    {
        var selectedNodes = _nodes.Where(selected);
        var source = Expression.Parameter(typeof(TEntity), "source");
        var related = Expression.Parameter(typeof(TEntity), "related");
        var nonLeaf = Expression.Lambda<Func<TEntity, bool>>(NonLeafBounds(source), source);

        var keyName = _descriptor.NodeKey.Name;
        Expression<Func<TEntity, object>> key = node => EF.Property<object>(node, keyName);
        var selectedKeys = selectedNodes
            .Select(key)
            .Distinct();

        var nonLeafKeys = selectedNodes
            .Where(nonLeaf)
            .Select(key)
            .Distinct();

        // WHY: Fence only the selected nonleaf keys. Materializing every structural pair prevented
        // selective payload joins from reaching the related alias; joining selected leaves by range
        // instead multiplied broad payload by all anchors. Rehydration retains mapped navigation identity.
        var nonLeafNodes = _nodes.Join(
            nonLeafKeys,
            key,
            nodeKey => nodeKey,
            (node, nodeKey) => node);

        // WHY: EF retains UNION row identifiers only when both branches expose matching identifier sets.
        // The self branch needs the same keyset join as the strict branch so anchor collections remain
        // shapeable. Only selected keys are fenced; the full structural pair relation stays correlated.
        var selfNodes = _nodes.Join(
            selectedKeys,
            key,
            nodeKey => nodeKey,
            (node, nodeKey) => node);

        var self = Expression.Call(
            s_where,
            selfNodes.Expression,
            Expression.Quote(
                Expression.Lambda<Func<TEntity, bool>>(RoleEqual(source, related, _descriptor.NodeKey), source)));

        var ancestors = Expression.Call(
            s_where,
            nonLeafNodes.Expression,
            Expression.Quote(
                Expression.Lambda<Func<TEntity, bool>>(Contains(source, related, includeSelf: false), source)));

        // WHY: UNION inside the correlation retains entity row identifiers required by collection
        // projections. This correlation needs a lateral-capable physical strategy, which the adapter
        // selects from the current configured engine; other strategies retain the selected-key union.
        var candidates = Expression.Call(s_union, self, ancestors);
        var collection = Expression.Lambda<Func<TEntity, IEnumerable<TEntity>>>(candidates, related);
        var result = Expression.Lambda<Func<TEntity, TEntity, TResult>>(
            NestedSetQueryExpressions.Inline(
                projection,
                source,
                related,
                Expression.Subtract(Depth(related), Depth(source))),
            related,
            source);

        var selectMany = s_selectManyMethods.GetValue(
            typeof(TResult),
            static resultType => s_selectManyDefinition.MakeGenericMethod(
                typeof(TEntity),
                typeof(TEntity),
                resultType));

        return _nodes.Provider.CreateQuery<TResult>(
            Expression.Call(selectMany, _nodes.Expression, Expression.Quote(collection), Expression.Quote(result)));
    }

    /// <summary>Separates self from strict descendants so selected leaves never enter a range join.</summary>
    private IQueryable<TResult> SelectSelectedKeySubtreePairs<TResult>(
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    )
    {
        var anchors = _nodes.Where(selected);
        var anchor = Expression.Parameter(typeof(TEntity), "anchor");
        var nonLeaf = Expression.Lambda<Func<TEntity, bool>>(NonLeafBounds(anchor), anchor);

        var keyName = _descriptor.NodeKey.Name;
        Expression<Func<TEntity, object>> key = node => EF.Property<object>(node, keyName);
        var self = anchors.Join(
            _nodes,
            key,
            key,
            (source, related) => new
            {
                Source = EF.Property<object>(source, keyName),
                Related = EF.Property<object>(related, keyName),
            });

        var descendants = SelectRangePairs(
            anchors.Where(nonLeaf),
            (source, related, distance) => new
            {
                Source = EF.Property<object>(source, keyName),
                Related = EF.Property<object>(related, keyName),
            },
            ancestors: false,
            includeSelf: false);

        // WHY: Native MySQL plans read every scope/tree row for each selected leaf before testing bounds.
        // Each structural key pair is unique and self/strict branches are disjoint, so this internal UNION
        // cannot deduplicate caller occurrences or equal projected values. Only non-leaves enter the range join.
        // WHY: EF's UNION ALL loses row identifiers needed by later collection projections. Two aliases on
        // both scalar UNION branches preserve those identifiers; joining back afterward preserves navigations
        // without carrying entity payload through the key relation or evaluating a client projection early.
        var sources = _nodes.Join(
            self.Union(descendants),
            key,
            pair => pair.Source,
            (source, pair) => new
            {
                Source = source,
                RelatedKey = pair.Related,
            });

        var pairs = sources.Join(
            _nodes,
            pair => pair.RelatedKey,
            key,
            (pair, related) => new
            {
                pair.Source,
                Related = related
            });

        return ProjectPairs(pairs, pair => pair.Source, pair => pair.Related, projection);
    }

    /// <summary>Inlines the caller's result after a structural pair relation has retained its mapped aliases.</summary>
    private IQueryable<TResult> ProjectPairs<TPair, TResult>(
        IQueryable<TPair> pairs,
        Expression<Func<TPair, TEntity>> upperSelector,
        Expression<Func<TPair, TEntity>> lowerSelector,
        Expression<Func<TEntity, TEntity, int, TResult>> projection,
        bool ancestors = false
    )
    {
        var occurrence = Expression.Parameter(typeof(TPair), "pair");
        var upper = NestedSetQueryExpressions.Inline(upperSelector, occurrence);
        var lower = NestedSetQueryExpressions.Inline(lowerSelector, occurrence);

        // WHY: Direction changes the caller's endpoint roles, not the structural positive distance.
        // Substitute the original projection once instead of building and traversing an intermediate swap.
        var result = Expression.Lambda<Func<TPair, TResult>>(
            NestedSetQueryExpressions.Inline(
                projection,
                ancestors ? lower : upper,
                ancestors ? upper : lower,
                Expression.Subtract(Depth(lower), Depth(upper))),
            occurrence);

        return pairs.Select(result);
    }

    /// <summary>Builds a portable structural range join over an already selected anchor relation.</summary>
    private IQueryable<TResult> SelectRangePairs<TResult>(
        IQueryable<TEntity> selectedNodes,
        Expression<Func<TEntity, TEntity, int, TResult>> projection,
        bool ancestors,
        bool includeSelf
    )
    {
        var first = Expression.Parameter(typeof(TEntity), "source");
        var second = Expression.Parameter(typeof(TEntity), "related");
        var upper = ancestors ? second : first;
        var lower = ancestors ? first : second;

        // WHY: Both aliases of a tree-bound root already have the exact same scope and tree filter.
        // Repeating their identity equality offers PostgreSQL a constant-key hash join, expanding
        // every selected anchor against the whole tree before applying the coordinate range.
        // Forest joins and independently supplied endpoints still require explicit identity equality.
        var containment = _treeBound ? ContainedBounds(upper, lower, includeSelf) : Contains(upper, lower, includeSelf);

        var relatedNodes = _nodes;
        if (ancestors && !includeSelf)
        {
            // WHY: A valid structural leaf cannot contain a strict descendant. This independent owner
            // predicate lets the server reject leaves before expanding the selected origin relation.
            var nonLeaf = Expression.Lambda<Func<TEntity, bool>>(NonLeafBounds(upper), second);

            relatedNodes = relatedNodes.Where(nonLeaf);
        }

        var range = Expression.Lambda<Func<TEntity, bool>>(containment, second);
        var collection = Expression.Lambda<Func<TEntity, IEnumerable<TEntity>>>(
            Expression.Call(s_where, relatedNodes.Expression, Expression.Quote(range)),
            first);

        var result = Expression.Lambda<Func<TEntity, TEntity, TResult>>(
            NestedSetQueryExpressions.Inline(
                projection,
                first,
                second,
                Expression.Subtract(Depth(lower), Depth(upper))),
            first,
            second);

        // WHY: Outer payload and distance belong in the result selector. Correlating an inner Select
        // instead can retain APPLY, which SQLite cannot execute.
        var selectMany = s_selectManyMethods.GetValue(
            typeof(TResult),
            static resultType => s_selectManyDefinition.MakeGenericMethod(
                typeof(TEntity),
                typeof(TEntity),
                resultType));

        return _nodes.Provider.CreateQuery<TResult>(
            Expression.Call(
                selectMany,
                selectedNodes.Expression,
                Expression.Quote(collection),
                Expression.Quote(result)));
    }

    /// <summary>Resolves the unique selected key set before joining expensive coordinate ranges.</summary>
    private IQueryable<TEntity> SelectedAnchors(
        Expression<Func<TEntity, bool>> selected
    )
    {
        // WHY: A correlated anchor predicate must remain available for join decorrelation. A DISTINCT
        // boundary around an upward query made PostgreSQL rescan a million-node interval per origin.
        // This is a conservative syntax-based optimization choice, not a validator or a claim that
        // arbitrary application predicates cannot expand into subqueries during EF translation.
        // WHY: This boundary addresses PostgreSQL's measured anchor-access plan. SQLite regressed
        // on broad facts with the same boundary; other engines retain their qualified original join.
        if (!_capabilities.UsesQueryAnchorKeyset
            || NestedSetQuerySelection.UsesQueryOperators(selected))
        {
            return _nodes.Where(selected);
        }

        var node = Expression.Parameter(typeof(TEntity), "selected");
        var key = Expression.Lambda<Func<TEntity, object>>(
            NestedSetExpressions.Property(node, _descriptor.NodeKey.Resolve(_entityType), typeof(object)),
            node);

        // WHY: Selection starts from a unique entity set, and its key is unique within the
        // required scope. DISTINCT on those keys preserves that set while keeping a selective key
        // relation visible to database optimizers. Without this boundary an interval lookup can
        // scan a large population and apply a handful of selected keys only as a residual.
        // No projected occurrence, payload row or path is deduplicated.
        var keys = _nodes
            .Where(selected)
            .Select(key)
            .Distinct();

        return _nodes.Join(
            keys,
            key,
            value => value,
            (anchor, _) => anchor);
    }
}
