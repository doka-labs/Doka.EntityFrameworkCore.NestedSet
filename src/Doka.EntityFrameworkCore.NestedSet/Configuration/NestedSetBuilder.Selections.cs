namespace Doka.EntityFrameworkCore.NestedSet;

public sealed partial class NestedSetBuilder<TEntity>
    where TEntity : class
{
    /// <summary>Stores a direct property selection until configuration is completed.</summary>
    /// <typeparam name="TProperty">The selected property's CLR type.</typeparam>
    /// <param name="role">The nested-set role fulfilled by the property.</param>
    /// <param name="expression">A direct property selector on the entity parameter.</param>
    /// <returns>This builder for continued configuration.</returns>
    private NestedSetBuilder<TEntity> Map<TProperty>(
        string role,
        Expression<Func<TEntity, TProperty>> expression
    )
    {
        _properties[role] = Select(expression);

        return this;
    }

    /// <summary>Stores an already mapped property selection until configuration is completed.</summary>
    /// <param name="role">The nested-set role fulfilled by the property.</param>
    /// <param name="propertyName">The exact EF property name.</param>
    /// <returns>This builder for continued configuration.</returns>
    private NestedSetBuilder<TEntity> Map(
        string role,
        string propertyName
    )
    {
        _properties[role] = Select(propertyName);

        return this;
    }

    /// <summary>Extracts a property name without accepting navigation traversal or computed expressions.</summary>
    /// <typeparam name="TProperty">The selected property's CLR type.</typeparam>
    /// <param name="expression">The selector to inspect.</param>
    /// <returns>The property name and declared selector result type.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector is not a direct property access on the entity.</exception>
    private static (string Name, Type Type) Select<TProperty>(
        Expression<Func<TEntity, TProperty>> expression
    )
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (expression.Body is not MemberExpression { Member: PropertyInfo property } member
            || member.Expression != expression.Parameters[0])
        {
            throw new ArgumentException("Select a direct entity property.", nameof(expression));
        }

        return (property.Name, typeof(TProperty));
    }

    /// <summary>Resolves a string-based selection without creating an implicit EF property.</summary>
    /// <param name="propertyName">The exact mapped property name.</param>
    /// <returns>The property name and its configured CLR type.</returns>
    /// <exception cref="ArgumentException">The name is empty or its property is not mapped.</exception>
    private (string Name, Type Type) Select(
        string propertyName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        var property = _builder.Metadata.FindProperty(propertyName)
            ?? throw new ArgumentException(
                $"Nested-set property '{propertyName}' must be mapped before selecting it by name.",
                nameof(propertyName));

        return (property.Name, property.ClrType);
    }

    /// <summary>Resolves a role from this callback or from configuration already present on the entity.</summary>
    /// <param name="role">The required nested-set role.</param>
    /// <returns>The selected mapped property name.</returns>
    /// <exception cref="InvalidOperationException">The role has not been configured.</exception>
    private string RequiredName(
        string role
    ) => _properties.TryGetValue(role, out var property)
        ? property.Name
        : _builder.Metadata.FindAnnotation(NestedSetAnnotationNames.Prefix + role)
            ?.Value as string
        ?? throw new InvalidOperationException($"Configure the nested-set {role} property.");

    /// <summary>Resolves an optional role from this callback or previous entity configuration.</summary>
    /// <param name="role">The optional nested-set role.</param>
    /// <returns>The selected mapped property name, or null when the role is absent.</returns>
    private string? OptionalName(
        string role
    ) => _properties.TryGetValue(role, out var property)
        ? property.Name
        : _builder.Metadata.FindAnnotation(NestedSetAnnotationNames.Prefix + role)
            ?.Value as string;
}
