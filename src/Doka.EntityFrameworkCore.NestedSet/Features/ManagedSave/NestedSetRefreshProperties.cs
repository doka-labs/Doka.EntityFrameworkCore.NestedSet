namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

/// <summary>Shares generated scalar metadata across tracked saves and detached subtype inputs.</summary>
internal static class NestedSetRefreshProperties
{
    /// <summary>Combines managed structure with generated tokens belonging to the same configured owner.</summary>
    /// <param name="owner">The exact hierarchy owner, including named shared or concrete TPC identity.</param>
    /// <param name="structure">The managed structural roles required by the caller.</param>
    /// <returns>Distinct scalar properties without ordinary application payload.</returns>
    internal static IProperty[] Collect(
        IEntityType owner,
        IEnumerable<IProperty> structure
    ) => structure
        .Concat(
            Generated(owner)
                .Where(property =>
                    property.IsConcurrencyToken && (property.ValueGenerated & ValueGenerated.OnUpdate) != 0))
        .Distinct()
        .ToArray();

    /// <summary>Finds generated scalars shared by tracked refresh and detached input capture or rollback.</summary>
    /// <param name="owner">The configured owner defining one exact hierarchy boundary.</param>
    /// <returns>Distinct generated properties from the owner and its participating subtypes.</returns>
    internal static IEnumerable<IProperty> Generated(
        IEntityType owner
    )
    {
        var mapping = NestedSetModelMapping.For(owner.Model);

        // WHY: The owner's flattened properties omit tokens declared only by concrete descendants. Independently
        // configured descendants retain their own refresh boundary even when they share an inherited property.
        return owner
            .GetDerivedTypesInclusive()
            .Where(entity => mapping.Owner(entity) == owner)
            .SelectMany(entity => entity.GetFlattenedProperties())
            .Where(property => property.ValueGenerated != ValueGenerated.Never)
            .Distinct();
    }

    /// <summary>Projects one mapped scalar while excluding rows of subtypes that do not declare it.</summary>
    /// <param name="entity">The owner-typed entity expression in a native or ordinal row.</param>
    /// <param name="property">The exact scalar metadata, including a nested complex leaf.</param>
    /// <returns>A boxed scalar projection with a derived discriminator guard when required.</returns>
    internal static Expression Project(
        Expression entity,
        IProperty property
    )
    {
        var declaring = DeclaringEntity(property);

        if (declaring.ClrType.IsAssignableFrom(entity.Type))
        {
            return Expression.Convert(NestedSetExpressions.Property(entity, property), typeof(object));
        }

        var derived = Expression.Convert(entity, declaring.ClrType);

        // WHY: A cast binds derived-only EF metadata, but shared sibling columns also need a discriminator
        // condition. SQL NULL for other subtypes avoids materializing a required scalar they do not map.
        return Expression.Condition(
            Expression.TypeIs(entity, declaring.ClrType),
            Expression.Convert(NestedSetExpressions.Property(derived, property), typeof(object)),
            Expression.Constant(null, typeof(object)));
    }

    /// <summary>Tests exact metadata inheritance before accessing a tracked subtype's property entry.</summary>
    /// <param name="property">The scalar metadata whose declaring subtype owns its tracking slots.</param>
    /// <param name="entity">The exact metadata of the tracked entry.</param>
    /// <returns>Whether the entry maps the scalar through its own metadata inheritance chain.</returns>
    internal static bool AppliesTo(
        IProperty property,
        IEntityType entity
    ) => DeclaringEntity(property)
        .IsAssignableFrom(entity);

    /// <summary>Tests a detached input's subtype without creating an EF entry retained by its context.</summary>
    /// <param name="property">The scalar metadata belonging to the exact configured input hierarchy.</param>
    /// <param name="entity">The detached input whose generated CLR values are captured or restored.</param>
    /// <returns>Whether the input's CLR subtype owns the scalar or containing complex property.</returns>
    internal static bool AppliesTo(
        IProperty property,
        object entity
    ) => DeclaringEntity(property)
        .ClrType
        .IsInstanceOfType(entity);

    /// <summary>Finds the declaring entity through an optional chain of complex scalar owners.</summary>
    /// <param name="property">The scalar whose containing entity owns its discriminator and tracker.</param>
    /// <returns>The entity declaring the scalar or its outermost complex property.</returns>
    private static IEntityType DeclaringEntity(
        IProperty property
    )
    {
        var declaring = property.DeclaringType;

        while (declaring is IComplexType complex)
        {
            declaring = complex.ComplexProperty.DeclaringType;
        }

        return (IEntityType)declaring;
    }
}
