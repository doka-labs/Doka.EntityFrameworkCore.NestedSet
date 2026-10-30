namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Identifies one mapped property without retaining mutable EF convention metadata.</summary>
/// <param name="Name">The stable EF model property name.</param>
/// <param name="ClrType">The exact mapped CLR type.</param>
/// <param name="IsNullable">Whether the model property accepts null.</param>
internal readonly record struct NestedSetPropertyDescriptor(
    string Name,
    Type ClrType,
    bool IsNullable
)
{
    /// <summary>Copies the stable scalar facets needed to resolve and verify the property later.</summary>
    /// <param name="property">The mutable or finalized EF property metadata.</param>
    /// <returns>An immutable property descriptor.</returns>
    internal static NestedSetPropertyDescriptor Create(
        IReadOnlyProperty property
    ) => new(property.Name, property.ClrType, property.IsNullable);

    /// <summary>Resolves this copied role against the mutable convention entity currently being finalized.</summary>
    /// <param name="entity">The convention entity that owns the role.</param>
    /// <returns>The matching mutable convention property.</returns>
    internal IConventionProperty Resolve(
        IConventionEntityType entity
    ) => Verify(entity, entity.FindProperty(Name));

    /// <summary>Resolves this copied role against the finalized runtime model that owns the descriptor.</summary>
    /// <param name="entity">The finalized entity that owns the role.</param>
    /// <returns>The matching finalized property.</returns>
    internal IProperty Resolve(
        IEntityType entity
    ) => Verify(entity, entity.FindProperty(Name));

    /// <summary>Guards against applying a descriptor to a different or subsequently changed model.</summary>
    private TProperty Verify<TProperty>(
        IReadOnlyEntityType entity,
        TProperty? property
    )
        where TProperty : class, IReadOnlyProperty
    {
        if (property is null)
        {
            throw new InvalidOperationException($"Nested-set property '{Name}' on '{entity.Name}' is not mapped.");
        }

        if (property.ClrType != ClrType
            || property.IsNullable != IsNullable)
        {
            throw new InvalidOperationException(
                $"Nested-set property '{Name}' on '{entity.Name}' does not match its descriptor.");
        }

        return property;
    }
}
