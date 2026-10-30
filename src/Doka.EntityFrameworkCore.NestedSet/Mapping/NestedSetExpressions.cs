namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Builds mapped property access consistently for structural projections, predicates, and updates.</summary>
internal static class NestedSetExpressions
{
    private static readonly MethodInfo s_property = typeof(EF)
        .GetMethod(nameof(EF.Property), [typeof(object), typeof(string)])!;
    private static readonly ConditionalWeakTable<Type, MethodInfo> s_propertyMethods = new();

    /// <summary>Creates an EF property access with the model's exact nullable or non-nullable CLR type.</summary>
    /// <param name="parameter">The entity parameter owned by the surrounding expression.</param>
    /// <param name="name">The mapped property or backing-field property name.</param>
    /// <param name="type">The property's exact CLR type.</param>
    /// <returns>A provider-translatable property access without a compiled CLR getter.</returns>
    internal static MethodCallExpression Property(
        Expression parameter,
        string name,
        Type type
    )
    {
        // WHY: Name-based Expression.Call repeats generic overload discovery for every structural
        // access. Cache only immutable public method metadata; weak keys do not pin collectible model types.
        var method = s_propertyMethods.GetValue(type, static scalar => s_property.MakeGenericMethod(scalar));

        return Expression.Call(method, parameter, Expression.Constant(name));
    }

    /// <summary>Builds scalar access through its complete non-collection complex-property chain.</summary>
    /// <param name="entity">The root entity expression.</param>
    /// <param name="property">The mapped scalar, including a leaf declared by a complex type.</param>
    /// <returns>An EF-translatable nested property access using exact model types and names.</returns>
    internal static MethodCallExpression Property(
        Expression entity,
        IProperty property
    ) => Property(ContainingInstance(entity, property.DeclaringType), property.Name, property.ClrType);

    /// <summary>Builds a translation marker through the mapped path with a caller-selected result type.</summary>
    /// <param name="entity">The mapped entity alias.</param>
    /// <param name="property">The finalized scalar metadata.</param>
    /// <param name="resultType">The expression type needed by the surrounding SQL-only composition.</param>
    /// <returns>An EF property marker; the provider still resolves its actual column mapping from metadata.</returns>
    internal static MethodCallExpression Property(
        Expression entity,
        IProperty property,
        Type resultType
    ) => Property(ContainingInstance(entity, property.DeclaringType), property.Name, resultType);

    /// <summary>Reads an exact mapped CLR member in a query, retaining EF markers for shadow or complex storage.</summary>
    /// <param name="entity">The mapped query alias, never an application value to evaluate.</param>
    /// <param name="property">The exact finalized scalar metadata.</param>
    /// <returns>A typed server-side member access or the equivalent EF property marker.</returns>
    internal static Expression QueryProperty(
        Expression entity,
        IProperty property
    )
    {
        var member = (MemberInfo?)property.PropertyInfo ?? property.FieldInfo;

        if (member?.DeclaringType is { } declaringType && declaringType.IsAssignableFrom(entity.Type)
            && property.DeclaringType.FindProperty(member.Name) == property
            && !property.IsIndexerProperty())
        {
            // WHY: EF translates the exact mapped member to the same column as EF.Property. A member
            // avoids repeated method-argument and attribute inspection during every warm parameter pass.
            // Indexers require a key, and complex/shadow roles retain their existing metadata path.
            return Expression.MakeMemberAccess(entity, member);
        }

        return Property(entity, property);
    }

    /// <summary>
    ///     Finds the complex instance containing a scalar while retaining field-backed EF property access.
    /// </summary>
    private static Expression ContainingInstance(
        Expression entity,
        ITypeBase type
    )
    {
        if (type is not IComplexType complex)
        {
            return entity;
        }

        var property = complex.ComplexProperty;

        return Property(ContainingInstance(entity, property.DeclaringType), property.Name, property.ClrType);
    }
}
