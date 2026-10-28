namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    private static readonly MethodInfo s_selectManyDefinition = typeof(Queryable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(Queryable.SelectMany)
            && method.GetParameters().Length == 3
            && method
                .GetParameters()[1]
                .ParameterType
                .GenericTypeArguments[0].GenericTypeArguments
                .Length == 2);

    private static readonly ConditionalWeakTable<Type, MethodInfo> s_selectManyMethods = new();

    // WHY: Name-based Expression.Call repeatedly scans Queryable's generic overloads during warm
    // report construction. These templates retain only exact public method metadata, never query state.
    private static readonly MethodInfo s_where = MethodOf(
        (Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>>>)(nodes => nodes.Where(node => true)));

    private static readonly MethodInfo s_union = MethodOf(
        (Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, IQueryable<TEntity>>>)((
            first,
            second
        ) => first.Union(second)));

    private static readonly MethodInfo s_orderByDepth = MethodOf(
        (Expression<Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>>)(nodes =>
            nodes.OrderByDescending(node => 0)));

    private static readonly MethodInfo s_orderByLeft = MethodOf(
        (Expression<Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>>)(nodes =>
            nodes.OrderByDescending(node => 0L)));

    private static readonly MethodInfo s_selectDecision = MethodOf(
        (Expression<Func<IQueryable<TEntity>, IQueryable<long>>>)(nodes => nodes.Select(node => 0L)));

    private static readonly MethodInfo s_firstDecision = MethodOf(
        (Expression<Func<IQueryable<long>, long>>)(decisions => decisions.FirstOrDefault()));

    private IQueryable<NestedSetQueryEdge<TEntity>>? _edges;

    /// <summary>Gets the presentation view without adding ordering to relational composition.</summary>
    internal IQueryable<TEntity> OrderedNodes => _nodes.OrderBy(Left());

    /// <summary>Projects selected anchors and inclusive descendants using mapped structural distance.</summary>
    internal IQueryable<TResult> SelectSubtrees<TResult>(
        Expression<Func<TEntity, bool>> anchors,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    ) => SelectPairs(anchors, projection, ancestors: false, includeSelf: true, nameof(anchors));

    /// <summary>Projects selected nodes with their strict visible ancestors.</summary>
    internal IQueryable<TResult> SelectAncestors<TResult>(
        Expression<Func<TEntity, bool>> nodes,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    ) => SelectPairs(nodes, projection, ancestors: true, includeSelf: false, nameof(nodes));

    /// <summary>Projects selected nodes with their visible ancestors and themselves.</summary>
    internal IQueryable<TResult> SelectAncestorsOrSelf<TResult>(
        Expression<Func<TEntity, bool>> nodes,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    ) => SelectPairs(nodes, projection, ancestors: true, includeSelf: true, nameof(nodes));

    /// <summary>Composes EF-expanded sources without inserting another deferred strategy marker.</summary>
    internal IQueryable<TResult> SelectNormalizedAncestors<TResult>(
        Expression<Func<TEntity, bool>> nodes,
        Expression<Func<TEntity, TEntity, int, TResult>> projection,
        bool includeSelf
    ) => SelectPairs(nodes, projection, ancestors: true, includeSelf, nameof(nodes), normalized: true);

    /// <summary>
    ///     Builds a range join with correlations confined to the inner Where for portable JOIN translation.
    /// </summary>
    private IQueryable<TResult> SelectPairs<TResult>(
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection,
        bool ancestors,
        bool includeSelf,
        string parameterName,
        bool normalized = false
    )
    {
        ArgumentNullException.ThrowIfNull(selected, parameterName);
        ArgumentNullException.ThrowIfNull(projection);
        RequireScope();

        if (!normalized
            && ancestors
            && _descriptor.AncestorIndexProperty is not null
            && (_capabilities.CompletedSourceMethod is null || _capabilities.OrderedJoinMethod is null))
        {
            NestedSetQueryDiagnostics.AncestorIndexWithoutReader(_context);
        }

        if (ancestors && TrySelectIndexedAncestorPairs(selected, projection, includeSelf, out var indexed))
        {
            return indexed;
        }

        if (!normalized
            && ancestors
            && NestedSetQuerySelection.UsesQueryOperators(selected))
        {
            // WHY: Captured fact queries and ordinary DbSet getters are not safe construction-time
            // evidence. EF expands them at terminal execution; choose the indexed or fallback plan only then.
            return NestedSetDeferredQuery.CreateAncestors(
                _nodes,
                _unboundNodes,
                selected,
                projection,
                _entityType.Name,
                _treeBound,
                includeSelf);
        }

        if (ancestors
            && !includeSelf
            && _capabilities.UsesIndependentAncestorOwners)
        {
            return SelectIndependentAncestorPairs(selected, projection);
        }

        // WHY: A relational selection must remain decorrelatable through a later direction change.
        // Materializing its UNION before an outer target filter expanded a million-node pivot per origin.
        if (!ancestors
            && !NestedSetQuerySelection.UsesQueryOperators(selected))
        {
            var strategy = _capabilities.ResolveSubtreeQueryStrategy(_context);

            if (strategy == NestedSetSubtreeQueryStrategy.RelatedFirstUnion)
            {
                return SelectRelatedFirstSubtreePairs(selected, projection);
            }

            if (strategy == NestedSetSubtreeQueryStrategy.SelectedKeyUnion)
            {
                return SelectSelectedKeySubtreePairs(selected, projection);
            }
        }

        // WHY: Upward pairs must remain available for pivot decorrelation in a later downward leg.
        // Materializing their selected-key boundary made mixed queries rescan a large ancestor range.
        var selectedNodes = ancestors ? _nodes.Where(selected) : SelectedAnchors(selected);

        return SelectRangePairs(selectedNodes, projection, ancestors, includeSelf);
    }

    /// <summary>Reads an exact standard LINQ method from a typed template without compiling it.</summary>
    private static MethodInfo MethodOf(
        LambdaExpression template
    ) => ((MethodCallExpression)template.Body).Method;
}
