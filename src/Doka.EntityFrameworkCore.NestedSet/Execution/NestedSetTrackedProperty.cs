namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Respects the original-value storage chosen by the application's EF tracking strategy.</summary>
internal static class NestedSetTrackedProperty
{
    /// <summary>Finds a scalar entry through its complete mapped complex-property chain.</summary>
    /// <param name="entry">The tracked root entity that owns the scalar value.</param>
    /// <param name="property">The exact scalar metadata, including nested complex leaves.</param>
    /// <returns>The public EF property entry for current values, originals, and modification flags.</returns>
    internal static PropertyEntry Property(
        EntityEntry entry,
        IProperty property
    ) => property.DeclaringType is IComplexType complex
        ? ComplexProperty(entry, complex).Property(property)
        : entry.Property(property.Name);

    /// <summary>Traverses model metadata instead of treating complex leaf names as root properties.</summary>
    private static ComplexPropertyEntry ComplexProperty(
        EntityEntry entry,
        IComplexType complex
    )
    {
        var property = complex.ComplexProperty;

        return property.DeclaringType is IComplexType parent
            ? ComplexProperty(entry, parent).ComplexProperty(property)
            : entry.ComplexProperty(property);
    }

    /// <summary>Identifies properties for which EF allocates an original-value slot.</summary>
    /// <param name="property">The mapped property whose original value will be read or refreshed.</param>
    /// <returns>Whether the property's original value is available through its tracked entry.</returns>
    internal static bool HasOriginalValue(
        IProperty property
    )
    {
        // WHY: ChangingAndChangedNotifications omits originals except relationship and concurrency data.
        return property.DeclaringType.GetChangeTrackingStrategy()
            != ChangeTrackingStrategy.ChangingAndChangedNotifications
            || property.IsConcurrencyToken
            || property.IsKey()
            || property.IsForeignKey()
            || property
                .GetContainingIndexes()
                .Any(index => index.IsUnique);
    }
}
