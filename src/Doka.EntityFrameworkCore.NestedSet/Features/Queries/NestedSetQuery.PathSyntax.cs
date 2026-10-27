namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    // WHY: These validated nodes have no free caller alias, model, query root or request value.
    // Sharing them avoids boxed Int64 constants and library-member discovery in every path proof.
    private static readonly ConstantExpression s_invalidPath = Expression.Constant(-1L);
    private static readonly ConstantExpression s_zeroDistance = Expression.Constant(0L);
    private static readonly ConstantExpression s_oneEdge = Expression.Constant(1L);
    private static readonly ConstantExpression s_twoEdges = Expression.Constant(2L);

    private static readonly PropertyInfo s_edgeParent =
        typeof(NestedSetQueryEdge<TEntity>).GetProperty(nameof(NestedSetQueryEdge<TEntity>.Parent))!;

    private static readonly PropertyInfo s_edgeChild =
        typeof(NestedSetQueryEdge<TEntity>).GetProperty(nameof(NestedSetQueryEdge<TEntity>.Child))!;

    /// <summary>Owns a closed direct-edge projection, whose aliases cannot capture an enclosing path.</summary>
    private static readonly Expression<Func<TEntity, TEntity, NestedSetQueryEdge<TEntity>>> s_edgeProjection =
    (
        principal,
        dependent
    ) => new NestedSetQueryEdge<TEntity>
    {
        Parent = principal,
        Child = dependent,
    };
}
