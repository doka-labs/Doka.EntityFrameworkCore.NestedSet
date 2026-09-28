namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Builds mapped property access consistently for structural projections, predicates, and updates.</summary>
internal static class NestedSetExpressions
{
    /// <summary>Creates an EF property access with the model's exact nullable or non-nullable CLR type.</summary>
    /// <param name="parameter">The entity parameter owned by the surrounding expression.</param>
    /// <param name="name">The mapped property or backing-field property name.</param>
    /// <param name="type">The property's exact CLR type.</param>
    /// <returns>A provider-translatable property access without a compiled CLR getter.</returns>
    internal static MethodCallExpression Property(
        Expression parameter,
        string name,
        Type type
    ) => Expression.Call(typeof(EF), nameof(EF.Property), [type], parameter, Expression.Constant(name));

    /// <summary>Builds scalar access through its complete non-collection complex-property chain.</summary>
    /// <param name="entity">The root entity expression.</param>
    /// <param name="property">The mapped scalar, including a leaf declared by a complex type.</param>
    /// <returns>An EF-translatable nested property access using exact model types and names.</returns>
    internal static MethodCallExpression Property(
        Expression entity,
        IProperty property
    ) => Property(ContainingInstance(entity, property.DeclaringType), property.Name, property.ClrType);

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
