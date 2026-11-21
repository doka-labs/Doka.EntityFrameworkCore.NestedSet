namespace Doka.EntityFrameworkCore.NestedSet;

public sealed partial class NestedSetBuilder<TEntity>
    where TEntity : class
{
    /// <summary>Starts an ascending sibling order using a mapped domain property.</summary>
    /// <typeparam name="TProperty">The selected property's CLR type.</typeparam>
    /// <param name="property">A direct mapped property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    /// <remarks>
    /// Replaces any existing sibling-order criteria. Nested-set coordinates, parent, and scope are not valid criteria.
    /// Database collation and value conversions determine comparison semantics. Nullable criteria require an
    /// overload with explicit null placement. The node key is appended ascending when it is not selected explicitly.
    /// Insert-generated criteria use saved values; criteria generated on update are unsupported.
    /// The default placement policy is Strict.
    /// </remarks>
    public NestedSetBuilder<TEntity> OrderBy<TProperty>(
        Expression<Func<TEntity, TProperty>> property
    ) => SetOrder(property, false, false, null);

    /// <summary>Starts an ascending sibling order with explicit placement for null values.</summary>
    /// <typeparam name="TProperty">The selected nullable property's CLR type.</typeparam>
    /// <param name="property">A direct mapped nullable property selector.</param>
    /// <param name="nullSortOrder">Whether null values sort before or after non-null values.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The null placement is not defined.</exception>
    public NestedSetBuilder<TEntity> OrderBy<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        NullSortOrder nullSortOrder
    ) => SetOrder(property, false, false, nullSortOrder);

    /// <summary>Starts a descending sibling order using a mapped domain property.</summary>
    /// <typeparam name="TProperty">The selected property's CLR type.</typeparam>
    /// <param name="property">A direct mapped property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    /// <remarks>
    /// Replaces any existing sibling-order criteria. Comparison and node-key tie breaking follow the ascending
    /// order contract. Nullable criteria require explicit null placement.
    /// </remarks>
    public NestedSetBuilder<TEntity> OrderByDescending<TProperty>(
        Expression<Func<TEntity, TProperty>> property
    ) => SetOrder(property, true, false, null);

    /// <summary>Starts a descending sibling order with explicit placement for null values.</summary>
    /// <typeparam name="TProperty">The selected nullable property's CLR type.</typeparam>
    /// <param name="property">A direct mapped nullable property selector.</param>
    /// <param name="nullSortOrder">Whether null values sort before or after non-null values.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The null placement is not defined.</exception>
    public NestedSetBuilder<TEntity> OrderByDescending<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        NullSortOrder nullSortOrder
    ) => SetOrder(property, true, false, nullSortOrder);

    /// <summary>Appends an ascending criterion to the configured sibling order.</summary>
    /// <typeparam name="TProperty">The selected property's CLR type.</typeparam>
    /// <param name="property">A direct mapped property selector not already used in this order.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">
    ///     The selector is not a direct property or duplicates an existing criterion.
    /// </exception>
    /// <exception cref="InvalidOperationException">No preceding sibling order is configured.</exception>
    public NestedSetBuilder<TEntity> ThenBy<TProperty>(
        Expression<Func<TEntity, TProperty>> property
    ) => SetOrder(property, false, true, null);

    /// <summary>Appends an ascending nullable criterion with explicit null placement.</summary>
    /// <typeparam name="TProperty">The selected nullable property's CLR type.</typeparam>
    /// <param name="property">A direct mapped nullable property selector not already used in this order.</param>
    /// <param name="nullSortOrder">Whether null values sort before or after non-null values.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector is invalid or duplicates an existing criterion.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The null placement is not defined.</exception>
    /// <exception cref="InvalidOperationException">No preceding sibling order is configured.</exception>
    public NestedSetBuilder<TEntity> ThenBy<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        NullSortOrder nullSortOrder
    ) => SetOrder(property, false, true, nullSortOrder);

    /// <summary>Appends a descending criterion to the configured sibling order.</summary>
    /// <typeparam name="TProperty">The selected property's CLR type.</typeparam>
    /// <param name="property">A direct mapped property selector not already used in this order.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">
    ///     The selector is not a direct property or duplicates an existing criterion.
    /// </exception>
    /// <exception cref="InvalidOperationException">No preceding sibling order is configured.</exception>
    public NestedSetBuilder<TEntity> ThenByDescending<TProperty>(
        Expression<Func<TEntity, TProperty>> property
    ) => SetOrder(property, true, true, null);

    /// <summary>Appends a descending nullable criterion with explicit null placement.</summary>
    /// <typeparam name="TProperty">The selected nullable property's CLR type.</typeparam>
    /// <param name="property">A direct mapped nullable property selector not already used in this order.</param>
    /// <param name="nullSortOrder">Whether null values sort before or after non-null values.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector is invalid or duplicates an existing criterion.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The null placement is not defined.</exception>
    /// <exception cref="InvalidOperationException">No preceding sibling order is configured.</exception>
    public NestedSetBuilder<TEntity> ThenByDescending<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        NullSortOrder nullSortOrder
    ) => SetOrder(property, true, true, nullSortOrder);

    /// <summary>Selects whether explicit placements may override the configured sibling order.</summary>
    /// <param name="mode">The placement policy; Strict is the default.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined order mode.</exception>
    /// <remarks>An order must be configured before the complete configuration callback returns.</remarks>
    public NestedSetBuilder<TEntity> HasOrderMode(
        NestedSetOrderMode mode
    )
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        _orderMode = mode;

        return this;
    }

    /// <summary>Starts or extends an order without changing the EF model during the configuration callback.</summary>
    /// <typeparam name="TProperty">The selected property's CLR type.</typeparam>
    /// <param name="property">A direct property selector.</param>
    /// <param name="descending">Whether the criterion uses descending order.</param>
    /// <param name="append">Whether to append rather than replace the criteria.</param>
    /// <param name="nullSortOrder">The explicit null placement, or null for a required property.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector is invalid or repeats a criterion.</exception>
    /// <exception cref="InvalidOperationException">An append has no preceding order.</exception>
    private NestedSetBuilder<TEntity> SetOrder<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        bool descending,
        bool append,
        NullSortOrder? nullSortOrder
    )
    {
        if (nullSortOrder is not null
            && !Enum.IsDefined(nullSortOrder.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(nullSortOrder));
        }

        var selected = Select(property);
        var order = append
            ? _order ?? ExistingOrder()
            : [];

        if (append && order.Count == 0)
        {
            throw new InvalidOperationException(
                "Call OrderBy or OrderByDescending before appending a sibling-order criterion.");
        }

        if (order.Any(criterion => criterion.Name == selected.Name))
        {
            throw new ArgumentException("A sibling-order property can be selected only once.", nameof(property));
        }

        order.Add((selected.Name, descending, nullSortOrder));
        _order = order;

        return this;
    }

    /// <summary>Copies existing criteria so subsequent callbacks cannot modify earlier annotation arrays.</summary>
    /// <returns>The existing criteria, or an empty list when ordering is not configured.</returns>
    /// <exception cref="InvalidOperationException">Existing criteria annotations are malformed.</exception>
    private List<(string Name, bool Descending, NullSortOrder? NullSortOrder)> ExistingOrder()
    {
        var namesValue = _builder.Metadata.FindAnnotation(NestedSetAnnotationNames.OrderProperties)
            ?.Value;

        var descendingValue = _builder.Metadata.FindAnnotation(NestedSetAnnotationNames.OrderDescending)
            ?.Value;

        var nullSortValue = _builder.Metadata.FindAnnotation(NestedSetAnnotationNames.OrderNullSort)
            ?.Value;

        if (namesValue is null
            && descendingValue is null
            && nullSortValue is null)
        {
            return [];
        }

        if (namesValue is not string[] { Length: > 0 } names
            || descendingValue is not bool[] descending
            || nullSortValue is not int[] nullSort
            || names.Length != descending.Length
            || names.Length != nullSort.Length
            || nullSort.Any(value => value != -1 && !Enum.IsDefined((NullSortOrder)value)))
        {
            throw new InvalidOperationException(
                "Nested-set sibling ordering requires matching property, direction, and null-order arrays.");
        }

        return names
            .Select((name,
                index) => (name, descending[index],
                nullSort[index] == -1
                    ? (NullSortOrder?)null
                    : (NullSortOrder)nullSort[index]))
            .ToList();
    }

    /// <summary>Reads the previous placement policy without changing its stored representation.</summary>
    /// <returns>The configured policy, or Strict when no policy was configured.</returns>
    /// <exception cref="InvalidOperationException">The stored value is not a supported order mode.</exception>
    private NestedSetOrderMode ExistingOrderMode()
    {
        var value = _builder.Metadata.FindAnnotation(NestedSetAnnotationNames.OrderMode)
            ?.Value;

        return value switch
        {
            null => NestedSetOrderMode.Strict,
            int mode when Enum.IsDefined((NestedSetOrderMode)mode) => (NestedSetOrderMode)mode,
            _ => throw new InvalidOperationException("The nested-set sibling order mode is invalid."),
        };
    }
}
