using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Inspects collection membership before EF's span-to-enumerable normalization.</summary>
internal class CollectionMembershipProbe : ExpressionVisitor
{
    /// <summary>Gets collection membership calls while excluding unrelated string operations.</summary>
    internal List<MethodCallExpression> Memberships { get; } = [];

    /// <summary>Gets the original number of explicit collection parameter markers.</summary>
    internal int ParameterMarkers { get; private set; }

    /// <summary>Gets membership subqueries that must retain their queryable operator.</summary>
    internal int QueryableMemberships { get; private set; }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == nameof(Enumerable.Contains)
            && (node.Method.DeclaringType == typeof(Enumerable)
                || node.Method.DeclaringType == typeof(MemoryExtensions)))
        {
            Memberships.Add(node);
        }

        if (node.Method.DeclaringType == typeof(EF) && node.Method.Name == nameof(EF.Parameter))
        {
            ParameterMarkers++;
        }

        if (node.Method.DeclaringType == typeof(Queryable) && node.Method.Name == nameof(Queryable.Contains))
        {
            QueryableMemberships++;
        }

        return base.VisitMethodCall(node);
    }
}
