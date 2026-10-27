namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal static partial class NestedSetDeferredQuery
{
    private static readonly MethodInfo s_path = Definition(nameof(Path));
    private static readonly MethodInfo s_any = Definition(nameof(Any));
    private static readonly MethodInfo s_nearest = Definition(nameof(Nearest));

    /// <summary>Defers path construction while preserving the already validated endpoint selectors.</summary>
    internal static IQueryable<TRow> CreatePath<TEntity, TRow>(
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> anchor,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, TEntity, bool>> edgeAllowed,
        string entityName,
        bool treeBound
    )
        where TEntity : class
    {
        var marker = Expression.Call(
            s_path.MakeGenericMethod(typeof(TEntity), typeof(TRow)),
            visible.Expression,
            unbound.Expression,
            source.Expression,
            Expression.Quote(anchor),
            Expression.Quote(node),
            Expression.Quote(edgeAllowed),
            Expression.Constant(entityName),
            Expression.Constant(treeBound));

        return source.Provider.CreateQuery<TRow>(marker);
    }

    /// <summary>Defers ancestor existence construction until source visibility can be checked after normalization.</summary>
    internal static IQueryable<TRow> CreateAny<TEntity, TRow>(
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, bool>> predicate,
        string entityName,
        bool treeBound
    )
        where TEntity : class
    {
        var marker = Expression.Call(
            s_any.MakeGenericMethod(typeof(TEntity), typeof(TRow)),
            visible.Expression,
            unbound.Expression,
            source.Expression,
            Expression.Quote(node),
            Expression.Quote(predicate),
            Expression.Constant(entityName),
            Expression.Constant(treeBound));

        return source.Provider.CreateQuery<TRow>(marker);
    }

    /// <summary>Defers nearest-owner construction without weakening candidate-before-predicate semantics.</summary>
    internal static IQueryable<TRow> CreateNearest<TEntity, TRow>(
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, bool>> candidate,
        Expression<Func<TRow, TEntity, bool>> predicate,
        string entityName,
        bool treeBound
    )
        where TEntity : class
    {
        var marker = Expression.Call(
            s_nearest.MakeGenericMethod(typeof(TEntity), typeof(TRow)),
            visible.Expression,
            unbound.Expression,
            source.Expression,
            Expression.Quote(node),
            Expression.Quote(candidate),
            Expression.Quote(predicate),
            Expression.Constant(entityName),
            Expression.Constant(treeBound));

        return source.Provider.CreateQuery<TRow>(marker);
    }

    /// <summary>Marks the source-preserving path filter for normalized composition.</summary>
    private static IQueryable<TRow> Path<TEntity, TRow>(
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> anchor,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, TEntity, bool>> edgeAllowed,
        [NotParameterized] string entityName,
        [NotParameterized] bool treeBound
    )
        where TEntity : class =>
        throw new InvalidOperationException("A deferred hierarchy marker must be expanded by UseNestedSets().");

    /// <summary>Marks the source-preserving ancestor existence filter for normalized composition.</summary>
    private static IQueryable<TRow> Any<TEntity, TRow>(
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, bool>> predicate,
        [NotParameterized] string entityName,
        [NotParameterized] bool treeBound
    )
        where TEntity : class =>
        throw new InvalidOperationException("A deferred hierarchy marker must be expanded by UseNestedSets().");

    /// <summary>Marks the source-preserving nearest-owner filter for normalized composition.</summary>
    private static IQueryable<TRow> Nearest<TEntity, TRow>(
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, bool>> candidate,
        Expression<Func<TRow, TEntity, bool>> predicate,
        [NotParameterized] string entityName,
        [NotParameterized] bool treeBound
    )
        where TEntity : class =>
        throw new InvalidOperationException("A deferred hierarchy marker must be expanded by UseNestedSets().");

    /// <summary>Reuses the existing path proof after its inner deferred source has expanded.</summary>
    private static Expression ExpandPath<TEntity, TRow>(
        DbContext context,
        MethodCallExpression marker
    )
        where TEntity : class
    {
        var query = Restore<TEntity>(context, marker, out var provider);
        var source = provider.CreateQuery<TRow>(marker.Arguments[2]);
        var anchor = (Expression<Func<TRow, TEntity>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[3]);
        var node = (Expression<Func<TRow, TEntity>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[4]);
        var edge =
            (Expression<Func<TRow, TEntity, TEntity, bool>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[5]);

        return query.WherePathEdges(source, anchor, node, edge, normalized: true)
            .Expression;
    }

    /// <summary>Reuses existing ancestor existence composition after normalized source visibility is known.</summary>
    private static Expression ExpandAny<TEntity, TRow>(
        DbContext context,
        MethodCallExpression marker
    )
        where TEntity : class
    {
        var query = Restore<TEntity>(context, marker, out var provider);
        var source = provider.CreateQuery<TRow>(marker.Arguments[2]);
        var node = (Expression<Func<TRow, TEntity>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[3]);
        var predicate = (Expression<Func<TRow, TEntity, bool>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[4]);

        return query.WhereAnyAncestorOrSelf(source, node, predicate, normalized: true)
            .Expression;
    }

    /// <summary>Reuses existing nearest-owner composition after normalized source visibility is known.</summary>
    private static Expression ExpandNearest<TEntity, TRow>(
        DbContext context,
        MethodCallExpression marker
    )
        where TEntity : class
    {
        var query = Restore<TEntity>(context, marker, out var provider);
        var source = provider.CreateQuery<TRow>(marker.Arguments[2]);
        var node = (Expression<Func<TRow, TEntity>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[3]);
        var candidate = (Expression<Func<TRow, TEntity, bool>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[4]);
        var predicate = (Expression<Func<TRow, TEntity, bool>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[5]);

        return query.WhereNearestAncestorOrSelf(source, node, candidate, predicate, normalized: true)
            .Expression;
    }
}
