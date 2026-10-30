namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal static partial class NestedSetDeferredQuery
{
    private static readonly MethodInfo s_strict = Definition(nameof(Strict));
    private static readonly MethodInfo s_inclusive = Definition(nameof(Inclusive));

    /// <summary>Builds an ancestor marker without reading a captured query or application property.</summary>
    internal static IQueryable<TResult> CreateAncestors<TEntity, TResult>(
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection,
        string entityName,
        bool treeBound,
        bool includeSelf
    )
        where TEntity : class
    {
        var method = (includeSelf ? s_inclusive : s_strict).MakeGenericMethod(typeof(TEntity), typeof(TResult));
        var marker = Expression.Call(
            method,
            visible.Expression,
            unbound.Expression,
            Expression.Quote(selected),
            Expression.Quote(projection),
            Expression.Constant(entityName),
            Expression.Constant(treeBound));

        return visible.Provider.CreateQuery<TResult>(marker);
    }

    /// <summary>Marks strict ancestor composition for expansion before relational translation.</summary>
    private static IQueryable<TResult> Strict<TEntity, TResult>(
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection,
        [NotParameterized] string entityName,
        [NotParameterized] bool treeBound
    )
        where TEntity : class =>
        throw new InvalidOperationException("A deferred hierarchy marker must be expanded by UseNestedSets().");

    /// <summary>Marks inclusive ancestors without parameterizing strategy or exact mapping identity.</summary>
    private static IQueryable<TResult> Inclusive<TEntity, TResult>(
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection,
        [NotParameterized] string entityName,
        [NotParameterized] bool treeBound
    )
        where TEntity : class =>
        throw new InvalidOperationException("A deferred hierarchy marker must be expanded by UseNestedSets().");

    /// <summary>Chooses the existing provider strategy using only EF-normalized arguments.</summary>
    private static Expression ExpandAncestors<TEntity, TResult>(
        DbContext context,
        MethodCallExpression marker
    )
        where TEntity : class
    {
        var query = Restore<TEntity>(context, marker, out _);
        var selected = (Expression<Func<TEntity, bool>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[2]);
        var projection =
            (Expression<Func<TEntity, TEntity, int, TResult>>)NestedSetQueryExpressions.Unwrap(marker.Arguments[3]);

        return query.SelectNormalizedAncestors(
                selected,
                projection,
                marker.Method.GetGenericMethodDefinition() == s_inclusive)
            .Expression;
    }
}
