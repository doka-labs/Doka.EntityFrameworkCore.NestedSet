namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Removes only newly composed library closure fields after EF's parameter-extraction boundary.</summary>
internal sealed class NestedSetNormalizedQueryClosure : ExpressionVisitor
{
    /// <inheritdoc />
    public override Expression? Visit(
        Expression? node
    )
    {
        if (node is null
            || node.GetType() == typeof(QueryParameterExpression)
            || !NestedSetQuerySyntax.CanInspect(node))
        {
            return node;
        }

        return base.Visit(node);
    }

    /// <inheritdoc />
    protected override Expression VisitMember(
        MemberExpression node
    )
    {
        var owner = Visit(node.Expression);

        if (node.Member is not FieldInfo { IsStatic: false, DeclaringType: { } declaring } field
            || declaring.Assembly != typeof(NestedSetNormalizedQueryClosure).Assembly
            || !declaring.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            || owner is not ConstantExpression { Value: { } target })
        {
            return node.Update(owner);
        }

        // WHY: Composition occurs after EF funcletization. Only our own compiler-generated fields
        // may be folded here; reading a consumer property, field or virtual query getter would freeze
        // a different deferred source and duplicate EF's application evaluation responsibility.
        var value = field.GetValue(target);

        if (value is IQueryable query
            && value.GetType().Assembly == typeof(DbContext).Assembly)
        {
            // WHY: The exact runtime implementation is EF-owned, so this immutable expression getter
            // cannot dispatch to an application's IQueryable subclass. Its roots are already normalized.
            return Visit(query.Expression)!;
        }

        if (value is null
            || node.Type.IsValueType
            || value is string)
        {
            return Expression.Constant(value, node.Type);
        }

        return node.Update(owner);
    }
}
