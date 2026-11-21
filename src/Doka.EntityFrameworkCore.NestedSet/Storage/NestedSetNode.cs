namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Stores only persisted hierarchy structure, without materializing a domain entity.</summary>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <param name="Key">The node identity.</param>
/// <param name="Left">The inclusive left boundary.</param>
/// <param name="Right">The inclusive right boundary.</param>
/// <param name="Parent">The nullable stored parent key.</param>
/// <param name="Depth">The number of ancestors.</param>
/// <param name="Position">The zero-based sibling position.</param>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal readonly record struct NestedSetNode<TKey>(
    TKey Key,
    long Left,
    long Right,
    NestedSetParent<TKey> Parent,
    int Depth,
    long Position
)
    where TKey : notnull
{
    /// <summary>Projects nullable parent keys without boxing a value-type key for every node.</summary>
    /// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
    /// <param name="key">The mapped primary key.</param>
    /// <param name="parent">The mapped nullable parent property.</param>
    /// <param name="left">The mapped left-boundary name.</param>
    /// <param name="right">The mapped right-boundary name.</param>
    /// <param name="depth">The mapped depth name.</param>
    /// <param name="position">The mapped sibling-position name.</param>
    /// <returns>A structural projection with typed optional parent storage.</returns>
    internal static Expression<Func<TEntity, NestedSetNode<TKey>>> CreateProjection<TEntity>(
        IProperty key,
        IProperty parent,
        string left,
        string right,
        string depth,
        string position
    )
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");
        var optionalParent = NestedSetParent<TKey>.Property(parameter, parent);

        var body = Expression.New(
            typeof(NestedSetNode<TKey>)
                .GetConstructors()[0],
            NestedSetExpressions.Property(parameter, key.Name, typeof(TKey)),
            NestedSetExpressions.Property(parameter, left, typeof(long)),
            NestedSetExpressions.Property(parameter, right, typeof(long)),
            optionalParent,
            NestedSetExpressions.Property(parameter, depth, typeof(int)),
            NestedSetExpressions.Property(parameter, position, typeof(long)));

        return Expression.Lambda<Func<TEntity, NestedSetNode<TKey>>>(body, parameter);
    }
}
