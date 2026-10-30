namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Shares runtime-owned syntax checks without conflating distinct hierarchy provenance proofs.</summary>
internal static class NestedSetQuerySyntax
{
    /// <summary>Admits non-extensible runtime syntax and the exact EF nodes used before relational translation.</summary>
    internal static bool CanInspect(
        Expression node
    ) => (node.GetType().Assembly == typeof(Expression).Assembly
            || node.GetType() == typeof(EntityQueryRootExpression)
            || node.GetType() == typeof(QueryParameterExpression))
        && node is not DynamicExpression
        && (node is not MethodCallExpression call
            || call.Method.GetType().Assembly == typeof(MethodInfo).Assembly)
        && (node is not MemberExpression member
            || member.Member.GetType().Assembly == typeof(MemberInfo).Assembly)
        && (node is not NewExpression { Constructor: { } constructor }
            || constructor.GetType().Assembly == typeof(ConstructorInfo).Assembly)
        && (node is not IndexExpression { Indexer: { } indexer }
            || indexer.GetType().Assembly == typeof(PropertyInfo).Assembly);
}
