using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionVisibilityTraversalTests
{
    /// <summary>Creates one visible mapped source without executing or translating it.</summary>
    private static IQueryable<CompositionGuardNode> Nodes(
        CompositionGuardContext context
    )
        => context
            .NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1)
            .InTree(Guid.Empty)
            .Nodes;

    /// <summary>Embeds actual queries in a predicate so endpoint resolution does not visit the probes.</summary>
    private static IQueryable<CompositionGuardNode> Source(
        IQueryable<CompositionGuardNode> nodes,
        IEnumerable<Expression> sources
    )
    {
        var tests = sources
            .Select(Any)
            .ToArray();

        var predicate = tests.Aggregate(Expression.AndAlso);
        var node = Expression.Parameter(typeof(CompositionGuardNode), "node");

        return nodes.Where(Expression.Lambda<Func<CompositionGuardNode, bool>>(predicate, node));
    }

    /// <summary>Uses the public Queryable Any metadata without evaluating a query or compiling a delegate.</summary>
    private static Expression Any(
        Expression source
    )
        => Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(CompositionGuardNode)], source);

    /// <summary>Creates an ordinary immutable query operator with runtime-owned reflection metadata.</summary>
    private static MethodCallExpression Where(
        Expression source,
        Expression predicate
    )
    {
        var node = Expression.Parameter(typeof(CompositionGuardNode), "node");

        return Expression.Call(
            s_where,
            source,
            Expression.Quote(Expression.Lambda<Func<CompositionGuardNode, bool>>(predicate, node)));
    }

}
