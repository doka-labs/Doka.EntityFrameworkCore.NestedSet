namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Shares the nullable parent/principal join between public filtered queries and structural storage.</summary>
/// <typeparam name="TEntity">The mapped entity on both sides of the direct-parent relationship.</typeparam>
/// <typeparam name="TResult">The caller's server-side pair projection.</typeparam>
internal static class NestedSetParentExpressions<TEntity, TResult>
    where TEntity : class
{
    private static readonly ConditionalWeakTable<IProperty, JoinFactory> s_factories = new();

    /// <summary>Builds one direct-edge query with the exact mapped parent type and principal collation.</summary>
    internal static IQueryable<TResult> Join(
        IQueryable<TEntity> nodes,
        IProperty key,
        IProperty parent,
        string? collation,
        Expression<Func<TEntity, TEntity, TResult>> projection
    ) => s_factories
        .GetValue(parent, static property => new JoinFactory(property.ClrType))
        .Build(nodes, key, parent, collation, projection);

    /// <summary>Compares a mapped parent link with an already visible principal alias.</summary>
    internal static Expression Matches(
        Expression principal,
        Expression dependent,
        IProperty key,
        IProperty parent,
        string? collation
    ) => s_factories
        .GetValue(parent, static property => new JoinFactory(property.ClrType))
        .Match(principal, dependent, key, parent, collation);

    /// <summary>Keeps nullable, converted and operator-free parent equality in its exact mapped type.</summary>
    private static Expression MatchesTyped<TParent>(
        Expression principal,
        Expression dependent,
        IProperty key,
        IProperty parent,
        string? collation
    )
    {
        var principalKey = Expression.Convert(NestedSetExpressions.QueryProperty(principal, key), typeof(TParent));
        var parentKey = NestedSetExpressions.QueryProperty(dependent, parent);

        if (collation is not null)
        {
            // WHY: The referenced principal defines equality, including when the FK column has another collation.
            parentKey = Expression.Call(
                typeof(RelationalDbFunctionsExtensions),
                nameof(RelationalDbFunctionsExtensions.Collate),
                [typeof(TParent)],
                Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                parentKey,
                Expression.Constant(collation));
        }

        return NestedSetTypedEquality<TParent>.Equal(principalKey, parentKey);
    }

    /// <summary>Closes the ordinary LINQ join over the actual nullable parent storage type.</summary>
    private static IQueryable<TResult> JoinTyped<TParent>(
        IQueryable<TEntity> nodes,
        IProperty key,
        IProperty parent,
        string? collation,
        Expression<Func<TEntity, TEntity, TResult>> projection
    )
    {
        var principal = Expression.Parameter(typeof(TEntity), "principal");
        var child = Expression.Parameter(typeof(TEntity), "child");
        var principalKey = Expression.Convert(NestedSetExpressions.QueryProperty(principal, key), typeof(TParent));
        var parentKey = NestedSetExpressions.QueryProperty(child, parent);

        if (collation is not null)
        {
            // WHY: The referenced principal defines identity even when the parent column uses another collation.
            parentKey = Expression.Call(
                typeof(RelationalDbFunctionsExtensions),
                nameof(RelationalDbFunctionsExtensions.Collate),
                [typeof(TParent)],
                Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                parentKey,
                Expression.Constant(collation));
        }

        return nodes.Join(
            nodes,
            Expression.Lambda<Func<TEntity, TParent>>(principalKey, principal),
            Expression.Lambda<Func<TEntity, TParent>>(parentKey, child),
            projection);
    }

    /// <summary>Closes the generic join once per mapped parent property on its first use.</summary>
    private sealed class JoinFactory
    {
        /// <summary>Builds a typed direct-edge relation without closing generics per operation.</summary>
        internal readonly Func<IQueryable<TEntity>, IProperty, IProperty, string?,
            Expression<Func<TEntity, TEntity, TResult>>, IQueryable<TResult>> Build;

        /// <summary>Builds the same principal/FK equality for already visible endpoint aliases.</summary>
        internal readonly Func<Expression, Expression, IProperty, IProperty, string?, Expression> Match;

        internal JoinFactory(
            Type parentType
        )
        {
            Build = typeof(NestedSetParentExpressions<TEntity, TResult>).GetMethod(
                    nameof(JoinTyped),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(parentType)
                .CreateDelegate<Func<IQueryable<TEntity>, IProperty, IProperty, string?,
                    Expression<Func<TEntity, TEntity, TResult>>, IQueryable<TResult>>>();

            // WHY: Only immutable type-specialized builders are cached. No query, context or runtime key survives
            // a composition, and a short path does not need to join its already proven endpoint rows again.
            Match = typeof(NestedSetParentExpressions<TEntity, TResult>).GetMethod(
                    nameof(MatchesTyped),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(parentType)
                .CreateDelegate<Func<Expression, Expression, IProperty, IProperty, string?, Expression>>();
        }
    }
}
