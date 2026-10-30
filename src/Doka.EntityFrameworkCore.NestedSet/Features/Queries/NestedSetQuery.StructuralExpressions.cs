namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Tests nonleaf width without overflowing any representable signed boundary.</summary>
    private BinaryExpression NonLeafBounds(
        Expression entity
    )
    {
        var left = Bound(entity, _descriptor.Left);
        var successor = Expression.Condition(
            Expression.Equal(left, Expression.Constant(long.MaxValue)),
            left,
            Expression.Add(left, Expression.Constant(1L)));

        // WHY: Split fragments can hold repairable empty or reversed intervals at Int64.MaxValue.
        // No signed Right can exceed that Left. Scalar CASE guards the addition without relying
        // on SQL AND evaluation order or changing the integral comparison contract.
        return Expression.GreaterThan(Bound(entity, _descriptor.Right), successor);
    }

    /// <summary>Checks one finite containment window for entity roles and internal scalar projections.</summary>
    private static BinaryExpression ContainedBounds(
        Expression upperLeft,
        Expression upperRight,
        Expression lowerLeft,
        Expression lowerRight,
        bool includeSelf
    )
    {
        var left = includeSelf
            ? Expression.LessThanOrEqual(upperLeft, lowerLeft)
            : Expression.LessThan(upperLeft, lowerLeft);

        var right = includeSelf
            ? Expression.GreaterThanOrEqual(upperRight, lowerRight)
            : Expression.GreaterThan(upperRight, lowerRight);

        // WHY: Valid nested-set bounds have Left < Right, so containment also implies these overlaps.
        // Stating both limits exposes a finite range on either existing coordinate index. One-sided
        // ranges made broad requests scan almost the whole tree for each leaf near an interval end.
        return Expression.AndAlso(
            Expression.AndAlso(left, right),
            Expression.AndAlso(
                Expression.LessThan(lowerLeft, upperRight),
                Expression.GreaterThan(lowerRight, upperLeft)));
    }

    /// <summary>Matches a visible entity and its complete identity, including non-PK scope/tree roles.</summary>
    private BinaryExpression MatchEndpoint(
        Expression bound,
        Expression selected
    ) => Expression.AndAlso(Expression.Equal(bound, selected), SameTree(bound, selected));

    /// <summary>Matches endpoints already proven to belong to this exact bound root.</summary>
    private BinaryExpression MatchKnownEndpoint(
        Expression bound,
        Expression selected
    )
    {
        var identity = Expression.Equal(bound, selected);

        // WHY: Both internal edge aliases retain the explicit scope binding. Public endpoints have
        // already passed complete MatchEndpoint membership (or exact-root provenance) before this
        // path decision is admitted. A forest still requires TreeId, including malformed Parent links.
        return _treeBound ? identity : Expression.AndAlso(identity, RoleEqual(bound, selected, _descriptor.TreeId));
    }

    /// <summary>Tests structural containment in this exact mapped tree without assuming public role names.</summary>
    private BinaryExpression Contains(
        Expression upper,
        Expression lower,
        bool includeSelf
    )
    {
        var bounds = ContainedBounds(upper, lower, includeSelf);

        // WHY: Internal owners and edges retain the explicit scope binding of the same immutable root.
        // External endpoints pass the full membership barrier first. Forests still compare TreeId;
        // independently supplied endpoints must never use this helper without that membership proof.
        return _treeBound ? bounds : Expression.AndAlso(RoleEqual(upper, lower, _descriptor.TreeId), bounds);
    }

    /// <summary>Tests mapped coordinate containment after the caller establishes tree and scope identity.</summary>
    private BinaryExpression ContainedBounds(
        Expression upper,
        Expression lower,
        bool includeSelf
    ) => ContainedBounds(
        Bound(upper, _descriptor.Left),
        Bound(upper, _descriptor.Right),
        Bound(lower, _descriptor.Left),
        Bound(lower, _descriptor.Right),
        includeSelf);

    /// <summary>Keeps tree and optional scope identity in SQL even when they are not primary-key columns.</summary>
    private BinaryExpression SameTree(
        Expression first,
        Expression second
    )
    {
        var result = RoleEqual(first, second, _descriptor.TreeId);

        if (_descriptor.Scope is { } scope)
        {
            result = Expression.AndAlso(result, RoleEqual(first, second, scope));
        }

        return result;
    }

    /// <summary>Builds translation-only identity equality without invoking CLR key operators.</summary>
    private BinaryExpression RoleEqual(
        Expression first,
        Expression second,
        NestedSetPropertyDescriptor role
    ) => Expression.Equal(
        Expression.Convert(NestedSetExpressions.QueryProperty(first, role.Resolve(_entityType)), typeof(object)),
        Expression.Convert(NestedSetExpressions.QueryProperty(second, role.Resolve(_entityType)), typeof(object)));

    /// <summary>Reads a configured Int64 boundary using its finalized metadata.</summary>
    private Expression Bound(
        Expression entity,
        NestedSetPropertyDescriptor role
    ) => NestedSetExpressions.QueryProperty(entity, role.Resolve(_entityType));

    /// <summary>Reads the mapped structural depth without consulting a domain's public Depth property.</summary>
    private Expression Depth(
        Expression entity
    ) => NestedSetExpressions.QueryProperty(entity, _descriptor.Depth.Resolve(_entityType));

    /// <summary>Creates an ordinary correlated EXISTS in the same EF provider.</summary>
    private static MethodCallExpression Any<TSource>(
        IQueryable<TSource> source,
        ParameterExpression parameter,
        Expression predicate
    )
    {
        // WHY: Entity and direct-edge sources already carry their element type. A typed lambda avoids
        // repeated delegate-type inference while the closed public method retains no query or model state.
        var lambda = Expression.Lambda<Func<TSource, bool>>(predicate, parameter);

        return Expression.Call(AnyMethod<TSource>.Method, source.Expression, Expression.Quote(lambda));
    }

    /// <summary>Retains only the exact public existence overload for one statically known source type.</summary>
    /// <typeparam name="TSource">The entity or joined direct-edge source type.</typeparam>
    private static class AnyMethod<TSource>
    {
        /// <summary>Gets the closed predicate overload without retaining a source or caller predicate.</summary>
        internal static readonly MethodInfo Method = MethodOf(
            (Expression<Func<IQueryable<TSource>, bool>>)(source => source.Any(value => true)));
    }

    /// <summary>Preserves correlation and avoids a near-empty estimate for an opaque path predicate.</summary>
    private static BinaryExpression CorrelatedCondition(
        Expression condition
    )
    {
        // WHY: PostgreSQL pulled an outer endpoint EXISTS into a semijoin before containment, creating
        // every anchor/endpoint combination. A scalar CASE keeps that lookup correlated. Long results
        // also avoid SQL Server's folding of boolean CASE back into a plain search condition.
        // Decode its binary result as nonzero: endpoint and exact path-count equalities otherwise made
        // PostgreSQL estimate one surviving row and rescan the entire payload instead of seeking its key.
        return Expression.NotEqual(
            Expression.Condition(condition, Expression.Constant(1L), Expression.Constant(0L)),
            Expression.Constant(0L));
    }

    /// <summary>Rejects LINQ-to-Objects and direct foreign-provider sources before any enumeration.</summary>
    private void RequireSource<TRow>(
        IQueryable<TRow> source
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireScope();

        if (!ReferenceEquals(source.Provider, _nodes.Provider))
        {
            throw new ArgumentException(
                "The source must use the same EF context and query provider as this hierarchy.",
                nameof(source));
        }
    }
}
