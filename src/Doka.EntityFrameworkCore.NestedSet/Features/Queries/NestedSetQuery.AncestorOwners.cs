namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    private static readonly MethodInfo s_skipFencedSources = MethodOf(
        (Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>>>)(sources => sources.Skip(0)));

    private static readonly ConditionalWeakTable<Type, MethodInfo> s_orderByKeyMethods = new();

    /// <summary>Drives strict ancestors from visible nonleaf owners into each owner's own source interval.</summary>
    private IQueryable<TResult> SelectOwnerFirstAncestorPairs<TResult>(
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    )
    {
        var owner = Expression.Parameter(typeof(TEntity), "owner");
        var source = Expression.Parameter(typeof(TEntity), "source");
        var nonLeaf = Expression.Lambda<Func<TEntity, bool>>(NonLeafBounds(owner), owner);
        var contained = Expression.Lambda<Func<TEntity, bool>>(Contains(owner, source, includeSelf: false), source);
        var sources = _nodes.Where(selected).Where(contained);

        // WHY: Strict ancestors have no bounded range in any coordinate index, while each owner's
        // descendants form one Left range. A cached plan compiled for one source let SQL Server scan
        // every owner again per source. In the measured plans OFFSET 0 kept this APPLY correlated instead
        // of a source-first ancestor scan, and Left order exposed the owner's range. The optimizer still
        // chooses the physical access path.
        var order = Expression.Lambda<Func<TEntity, long>>(Bound(source, _descriptor.Left), source);
        var fenced = Expression.Call(s_skipFencedSources,
            Expression.Call(s_orderByLeft, sources.Expression, Expression.Quote(order)), Expression.Constant(0));

        var collection = Expression.Lambda<Func<TEntity, IEnumerable<TEntity>>>(fenced, owner);
        var pairs = _nodes.Where(nonLeaf).SelectMany(collection,
            (related, selectedSource) => new { Related = related, Source = selectedSource });

        return ProjectPairs(pairs, pair => pair.Related, pair => pair.Source, projection, ancestors: true);
    }

    /// <summary>Fences an independent fact selection before an inclusive ancestor range join.</summary>
    private IQueryable<TEntity> SelectedSources(
        Expression<Func<TEntity, bool>> selected
    )
    {
        var sources = _nodes.Where(selected);

        // WHY: SQL Server evaluated a fact EXISTS for every node of each selected anchor subtree and read
        // the fact tables once per node. Independent fact roots cannot correlate with a later direction
        // change, so this boundary loses no decorrelation; in the measured plans the facts were read once.
        // Hierarchy-backed selections stay correlatable.
        if (!_capabilities.CompletesFactSelections
            || NestedSetQuerySelection.RequiresAncestorDecorrelation(selected, _entityType.Model, out var factSelection)
            || !factSelection)
        {
            return sources;
        }

        return _nodes.Provider.CreateQuery<TEntity>(FencedByNodeKey(sources.Expression));
    }

    /// <summary>Orders a source relation by its mapped node key behind an OFFSET 0 completion boundary.</summary>
    private MethodCallExpression FencedByNodeKey(
        Expression sources
    )
    {
        var source = Expression.Parameter(typeof(TEntity), "source");
        var key = NestedSetExpressions.QueryProperty(source, _descriptor.NodeKey.Resolve(_entityType));
        var orderByKey = s_orderByKeyMethods.GetValue(key.Type,
            static type => s_orderByLeft.GetGenericMethodDefinition().MakeGenericMethod(typeof(TEntity), type));

        return Expression.Call(s_skipFencedSources,
            Expression.Call(orderByKey, sources, Expression.Quote(Expression.Lambda(key, source))),
            Expression.Constant(0));
    }
}
