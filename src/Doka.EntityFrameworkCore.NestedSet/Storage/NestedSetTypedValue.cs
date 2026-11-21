namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Keeps a known structural role typed across snapshots, comparisons and metadata reads.</summary>
/// <typeparam name="TValue">The mapped non-null identity type.</typeparam>
internal static class NestedSetTypedValue<TValue>
    where TValue : notnull
{
    private static readonly ConditionalWeakTable<IProperty, NestedSetProviderComparer<TValue>> s_comparers = new();

    /// <summary>Reads a known role through EF's public typed live-value API without boxing native values.</summary>
    /// <param name="values">The live current values shared by this entry's structural reads.</param>
    /// <param name="property">The exact finalized mapped property.</param>
    /// <returns>The current typed value, including shadow and temporary sidecar storage.</returns>
    internal static TValue Read(
        PropertyValues values,
        IProperty property
    ) => values.GetValue<TValue>(property);

    /// <summary>Reads one known hierarchy role without requiring the caller to retain live values.</summary>
    /// <typeparam name="TEntity">The mapped hierarchy entity whose identity role is already known.</typeparam>
    internal static TValue Read<TEntity>(
        EntityEntry<TEntity> entry,
        IProperty property
    )
        where TEntity : class

    // WHY: A single typed PropertyEntry avoids the complex-property lists allocated by CurrentValues.
    // Shared multirole readers use the PropertyValues overload instead of repeating either wrapper.
        => entry.Property<TValue>(property)
            .CurrentValue;

    /// <summary>Reads a CLR, field or property-bag identity without retaining an EF entry.</summary>
    internal static TValue Read(
        object entity,
        IProperty property
    ) => (TValue)NestedSetStructuralValue.Read(entity, property)!;

    /// <summary>Snapshots one known role while preserving independent mutable identity storage.</summary>
    internal static TValue Snapshot(
        IProperty property,
        TValue value
    )
    {
        if (value is byte[] bytes)
        {
            // WHY: A domain comparer may snapshoot arrays by reference; structural identities must not alias callers.
            return (TValue)bytes.Clone();
        }

        if (property.GetValueComparer() is ValueComparer<TValue> comparer)
        {
            return comparer.Snapshot(value);
        }

        // WHY: Nullable parent metadata and custom comparer implementations expose only EF's object snapshot API.
        // The cast remains at that metadata seam; callers retain their already known structural role type.
        return (TValue)property
            .GetValueComparer()
            .Snapshot(value);
    }

    /// <summary>Resolves one role's cached provider comparer for repeated typed identity checks.</summary>
    /// <param name="property">The finalized mapped identity property.</param>
    /// <returns>The model-lifetime comparer for this exact property and value type.</returns>
    internal static NestedSetProviderComparer<TValue> Comparer(
        IProperty property
    ) => s_comparers.GetValue(property, static metadata => new NestedSetProviderComparer<TValue>(metadata));

    /// <summary>Compares typed identities by their exact mapped provider representations.</summary>
    internal static bool Matches(
        IProperty property,
        TValue current,
        TValue expected
    ) => Comparer(property)
        .Equals(current, expected);

    /// <summary>Builds equality while keeping the mapped role's expression type intact.</summary>
    internal static Expression Equal(
        Expression left,
        Expression right
    ) => NestedSetTypedEquality<TValue>.Equal(left, right);
}
