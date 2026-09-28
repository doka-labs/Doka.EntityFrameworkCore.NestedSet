namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Resolves immutable sibling-order metadata and composes provider-native ordered queries.</summary>
internal sealed class NestedSetOrdering
{
    /// <summary>Stores selectors that contain only mapped names and never capture a context or scope.</summary>
    private readonly LambdaExpression[] _selectors;

    /// <summary>Stores provider-independent null-rank selectors only for nullable criteria.</summary>
    private readonly LambdaExpression?[] _nullRankSelectors;

    /// <summary>Creates the finalized order, including its unique ascending node-key tiebreaker.</summary>
    /// <param name="entity">The finalized entity whose CLR type defines the selectors.</param>
    /// <param name="properties">The private property array in comparison precedence order.</param>
    /// <param name="descending">The private direction array matching the property array.</param>
    /// <param name="nullSortOrders">The private explicit null placement matching the property array.</param>
    /// <param name="mode">The validated placement policy.</param>
    private NestedSetOrdering(
        IEntityType entity,
        IProperty[] properties,
        bool[] descending,
        NullSortOrder?[] nullSortOrders,
        NestedSetOrderMode mode
    )
    {
        // WHY: Read-only wrappers prevent consumers from changing the shared model's ordering arrays.
        Properties = Array.AsReadOnly(properties);
        Descending = Array.AsReadOnly(descending);
        NullSortOrders = Array.AsReadOnly(nullSortOrders);
        Mode = mode;
        _selectors = new LambdaExpression[properties.Length];
        _nullRankSelectors = new LambdaExpression?[properties.Length];
        var parameter = Expression.Parameter(entity.ClrType, "node");

        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            var access = NestedSetExpressions.Property(parameter, property.Name, property.ClrType);

            _selectors[index] = Expression.Lambda(access, parameter);

            if (nullSortOrders[index] is { } nullSortOrder)
            {
                // WHY: Providers use different implicit null ordering. An integer rank makes the configured
                // placement identical for LINQ queries, raw SQL windows, rebuilds, and automatic mutations.
                var isNull = Expression.Equal(access, Expression.Constant(null, property.ClrType));
                var nullRank = nullSortOrder == NullSortOrder.First ? 0 : 1;
                var nonNullRank = 1 - nullRank;

                _nullRankSelectors[index] = Expression.Lambda(
                    Expression.Condition(isNull, Expression.Constant(nullRank), Expression.Constant(nonNullRank)),
                    parameter);
            }
        }
    }

    /// <summary>Gets the ordered mapped properties, including an implicit ascending key when needed.</summary>
    internal IReadOnlyList<IProperty> Properties { get; }

    /// <summary>Gets whether each matching property is ordered descending.</summary>
    internal IReadOnlyList<bool> Descending { get; }

    /// <summary>Gets explicit null placement for each matching property.</summary>
    internal IReadOnlyList<NullSortOrder?> NullSortOrders { get; }

    /// <summary>Gets whether explicit placement may override the configured order.</summary>
    internal NestedSetOrderMode Mode { get; }

    /// <summary>Resolves configured order annotations from the finalized entity model.</summary>
    /// <param name="entity">The entity whose nested-set structural mapping has already been validated.</param>
    /// <param name="descriptor">The entity's immutable role and ordering contract.</param>
    /// <returns>The immutable order, or null when no property order is configured.</returns>
    /// <exception cref="InvalidOperationException">Order annotations or selected properties are invalid.</exception>
    internal static NestedSetOrdering? Create(
        IEntityType entity,
        NestedSetModelDescriptor descriptor
    )
    {
        if (descriptor.Order.Count == 0)
        {
            return null;
        }

        var properties = new IProperty[descriptor.Order.Count + 1];
        var descending = new bool[properties.Length];
        var nullSortOrders = new NullSortOrder?[properties.Length];

        for (var index = 0; index < descriptor.Order.Count; index++)
        {
            var order = descriptor.Order[index];
            properties[index] = order.Property.Resolve(entity);

            descending[index] = order.Descending;
            nullSortOrders[index] = order.NullSortOrder;
        }

        // WHY: Equal domain values still need a total database order, including native key collation semantics.
        properties[^1] = descriptor.NodeKey.Resolve(entity);

        return new NestedSetOrdering(
            entity,
            properties,
            descending,
            nullSortOrders,
            descriptor.OrderMode ?? NestedSetOrderMode.Strict);
    }

    /// <summary>Rejects invalid sibling-order properties before metadata is applied.</summary>
    /// <param name="entity">The mutable or finalized mapped entity.</param>
    /// <param name="names">The configured property names, excluding any implicit key tiebreaker.</param>
    /// <param name="structuralProperties">The selected mutable coordinates, parent, and scope property names.</param>
    /// <param name="nullSortOrders">The explicit null placement matching every configured property.</param>
    /// <param name="validateNullability">Whether EF has finalized each property's required/optional state.</param>
    /// <exception cref="InvalidOperationException">A selected property cannot define sibling order.</exception>
    internal static void ValidateProperties(
        IReadOnlyEntityType entity,
        IReadOnlyList<string> names,
        IReadOnlyList<string> structuralProperties,
        IReadOnlyList<NullSortOrder?> nullSortOrders,
        bool validateNullability
    )
    {
        if (names.Count != nullSortOrders.Count)
        {
            throw new InvalidOperationException("Sibling-order properties require a matching null-order vector.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);

        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];

            if (string.IsNullOrWhiteSpace(name)
                || !seen.Add(name))
            {
                throw new InvalidOperationException("Sibling-order properties must be nonempty and distinct.");
            }

            if (structuralProperties.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Sibling order must use domain properties rather than nested-set structural properties.");
            }

            // WHY: Converted domain values are compared after persistence by the provider. Update-generated
            // values could change during bounds shifts themselves, leaving no stable sibling order to enforce.
            var property = entity.FindProperty(name);
            if (property is null
                || property.IsShadowProperty()
                || property.IsIndexerProperty()
                || property.PropertyInfo is null
                || table is not { } store
                || property.GetColumnName(store) is null)
            {
                throw new InvalidOperationException(
                    $"Sibling-order property '{name}' must be a mapped, stored CLR property.");
            }

            if ((property.ValueGenerated & ValueGenerated.OnUpdate) != 0)
            {
                throw new InvalidOperationException(
                    $"Sibling-order property '{name}' must not be generated on update. "
                    + "Sort criteria must remain stable during structural updates.");
            }

            if (validateNullability
                && property.IsNullable
                && nullSortOrders[index] is null)
            {
                throw new InvalidOperationException(
                    $"Nullable sibling-order property '{name}' requires explicit null placement.");
            }

            if (validateNullability
                && !property.IsNullable
                && nullSortOrders[index] is not null)
            {
                throw new InvalidOperationException(
                    $"Required sibling-order property '{name}' must not configure null placement.");
            }
        }
    }

    /// <summary>Applies the complete order through query expressions that the database provider translates.</summary>
    /// <typeparam name="TEntity">The configured entity CLR type.</typeparam>
    /// <param name="query">The query selecting the siblings or nodes to order.</param>
    /// <returns>An ordered query without executing or materializing its rows.</returns>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    internal IOrderedQueryable<TEntity> Apply<TEntity>(
        IQueryable<TEntity> query
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);

        var expression = query.Expression;

        for (var index = 0; index < Properties.Count; index++)
        {
            if (_nullRankSelectors[index] is { } nullRankSelector)
            {
                expression = Apply<TEntity>(expression, nullRankSelector, typeof(int), false, index == 0);
            }

            expression = Apply<TEntity>(
                expression,
                _selectors[index],
                Properties[index].ClrType,
                Descending[index],
                index == 0 && _nullRankSelectors[index] is null);
        }

        return (IOrderedQueryable<TEntity>)query.Provider.CreateQuery<TEntity>(expression);
    }

    /// <summary>Appends one typed ordering expression without compiling or invoking the selector.</summary>
    private static MethodCallExpression Apply<TEntity>(
        Expression source,
        LambdaExpression selector,
        Type keyType,
        bool descending,
        bool first
    )
        where TEntity : class
    {
        var method = first
            ? descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy)
            : descending
                ? nameof(Queryable.ThenByDescending)
                : nameof(Queryable.ThenBy);

        return Expression.Call(
            typeof(Queryable),
            method,
            [typeof(TEntity), keyType],
            source,
            Expression.Quote(selector));
    }
}
