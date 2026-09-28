namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Maps an entity's persisted nested-set structure.</summary>
/// <typeparam name="TEntity">The mapped entity type.</typeparam>
/// <remarks>Configuration is applied after the callback succeeds. Builder instances are not thread-safe.</remarks>
public sealed class NestedSetBuilder<TEntity>
    where TEntity : class
{
    /// <summary>The EF builder that receives configuration after the callback completes.</summary>
    private readonly EntityTypeBuilder<TEntity> _builder;

    /// <summary>The pending CLR property selected for each structural role.</summary>
    private readonly Dictionary<string, (string Name, Type Type)> _properties = new(StringComparer.Ordinal);

    /// <summary>The pending sibling-order criteria, or null to retain previously configured criteria.</summary>
    private List<(string Name, bool Descending, NullSortOrder? NullSortOrder)>? _order;

    /// <summary>The pending policy for explicit placement when sibling order is configured.</summary>
    private NestedSetOrderMode? _orderMode;

    /// <summary>Whether application-managed concurrency tokens are explicitly preserved by structure-only writes.</summary>
    private bool _preserveApplicationConcurrencyTokens;

    /// <summary>Creates a pending nested-set configuration for an EF entity.</summary>
    /// <param name="builder">The entity builder that receives the completed mapping.</param>
    internal NestedSetBuilder(
        EntityTypeBuilder<TEntity> builder
    )
    {
        _builder = builder;
    }

    /// <summary>Selects the stable scalar key used by hierarchy operations.</summary>
    /// <typeparam name="TKey">The primary key type.</typeparam>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    /// <remarks>
    /// The node key may be a scalar primary key or a scalar alternate key. With a composite EF primary key,
    /// configure an alternate key over the node key, or over scope and node key when scope is configured.
    /// </remarks>
    public NestedSetBuilder<TEntity> HasNodeKey<TKey>(
        Expression<Func<TEntity, TKey>> property
    ) => Map(NestedSetPropertyRoles.NodeKey, property);

    /// <summary>Selects an already mapped scalar node-key property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasNodeKey(
        string propertyName
    ) => Map(NestedSetPropertyRoles.NodeKey, propertyName);

    /// <summary>Selects the stable tree identity stored on every node.</summary>
    /// <typeparam name="TTreeId">The tree identity type.</typeparam>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasTreeId<TTreeId>(
        Expression<Func<TEntity, TTreeId>> property
    ) => Map(NestedSetPropertyRoles.TreeId, property);

    /// <summary>Selects an already mapped tree-identity property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasTreeId(
        string propertyName
    ) => Map(NestedSetPropertyRoles.TreeId, propertyName);

    /// <summary>Selects the positive inclusive left and right boundaries.</summary>
    /// <param name="left">The direct left boundary property selector.</param>
    /// <param name="right">The direct right boundary property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">A selector is null.</exception>
    /// <exception cref="ArgumentException">A selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasBounds(
        Expression<Func<TEntity, long>> left,
        Expression<Func<TEntity, long>> right
    )
    {
        var leftProperty = Select(left);
        var rightProperty = Select(right);

        // WHY: Both selectors must be valid before either pending boundary mapping is changed.
        _properties[NestedSetPropertyRoles.Left] = leftProperty;
        _properties[NestedSetPropertyRoles.Right] = rightProperty;

        return this;
    }

    /// <summary>Selects already mapped inclusive left and right boundary properties by name.</summary>
    /// <param name="leftPropertyName">The mapped left boundary property name.</param>
    /// <param name="rightPropertyName">The mapped right boundary property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A name is empty or its property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasBounds(
        string leftPropertyName,
        string rightPropertyName
    )
    {
        var leftProperty = Select(leftPropertyName);
        var rightProperty = Select(rightPropertyName);

        // WHY: Both names must resolve before either pending boundary mapping is changed.
        _properties[NestedSetPropertyRoles.Left] = leftProperty;
        _properties[NestedSetPropertyRoles.Right] = rightProperty;

        return this;
    }

    /// <summary>Selects the optional application scope that partitions tree identities and node keys.</summary>
    /// <typeparam name="TScope">The scope key type.</typeparam>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasScope<TScope>(
        Expression<Func<TEntity, TScope>> property
    ) => Map(NestedSetPropertyRoles.Scope, property);

    /// <summary>Selects an already mapped optional scope property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasScope(
        string propertyName
    ) => Map(NestedSetPropertyRoles.Scope, propertyName);

    /// <summary>Selects the nullable parent key; null denotes a root.</summary>
    /// <typeparam name="TParent">The nullable primary key type.</typeparam>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasParent<TParent>(
        Expression<Func<TEntity, TParent>> property
    ) => Map(NestedSetPropertyRoles.Parent, property);

    /// <summary>Selects an already mapped nullable parent-node-key property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasParent(
        string propertyName
    ) => Map(NestedSetPropertyRoles.Parent, propertyName);

    /// <summary>Selects the persisted depth; roots have depth zero.</summary>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasDepth(
        Expression<Func<TEntity, int>> property
    ) => Map(NestedSetPropertyRoles.Depth, property);

    /// <summary>Selects an already mapped required Int32 depth property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasDepth(
        string propertyName
    ) => Map(NestedSetPropertyRoles.Depth, propertyName);

    /// <summary>Selects the persisted, dense, zero-based position among siblings in the same tree.</summary>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasPosition(
        Expression<Func<TEntity, long>> property
    ) => Map(NestedSetPropertyRoles.Position, property);

    /// <summary>Selects an already mapped required Int64 sibling-position property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasPosition(
        string propertyName
    ) => Map(NestedSetPropertyRoles.Position, propertyName);

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

    /// <summary>Declares that structure-only writes preserve application-managed concurrency-token values.</summary>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Normal EF payload saves continue to use the token for optimistic concurrency. Nested-set coordinate,
    /// position, parent, and TreeId updates do not assign a new token value. Store-generated concurrency tokens
    /// are refreshed independently and do not require this policy.
    /// </remarks>
    public NestedSetBuilder<TEntity> PreserveApplicationConcurrencyTokens()
    {
        _preserveApplicationConcurrencyTokens = true;

        return this;
    }

    /// <summary>Applies validated role selections and the indexes used by structural queries.</summary>
    /// <exception cref="InvalidOperationException">
    ///     A required role has neither a selection nor an existing annotation.
    /// </exception>
    internal void Complete()
    {
        var entity = _builder.Metadata;

        // WHY: Scoped nodes inherit the unscoped interface. Prefer the more specific contract so convention-based
        // mapping captures the complete tree identity instead of silently omitting Scope.
        var interfaces = typeof(TEntity).GetInterfaces();
        var scopedNestedSetInterface = interfaces.FirstOrDefault(type => type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(IScopedNestedSetNode<,,>));

        var nestedSetInterface = scopedNestedSetInterface
            ?? interfaces.FirstOrDefault(type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(INestedSetNode<,>));

        if (nestedSetInterface is not null)
        {
            var inferredRoles = new List<(string Role, string PropertyName)>
            {
                (NestedSetPropertyRoles.NodeKey, nameof(INestedSetNode<,>.Id)),
                (NestedSetPropertyRoles.TreeId, nameof(INestedSetNode<,>.TreeId)),
                (NestedSetPropertyRoles.Left, nameof(INestedSetNode<,>.Left)),
                (NestedSetPropertyRoles.Right, nameof(INestedSetNode<,>.Right)),
                (NestedSetPropertyRoles.Depth, nameof(INestedSetNode<,>.Depth)),
                (NestedSetPropertyRoles.Position, nameof(INestedSetNode<,>.Position)),
            };

            if (scopedNestedSetInterface is not null)
            {
                inferredRoles.Add(
                    (NestedSetPropertyRoles.Scope, nameof(IScopedNestedSetNode<object, object, object>.Scope)));
            }

            foreach (var (role, propertyName) in inferredRoles)
            {
                if (!_properties.ContainsKey(role)
                    && entity.FindAnnotation(NestedSetAnnotationNames.Prefix + role) is null)
                {
                    var propertyType = typeof(TEntity).GetProperty(propertyName)
                            ?.PropertyType
                        ?? throw new InvalidOperationException(
                            $"Nested-set interface property '{propertyName}' is not implemented as a CLR property.");

                    _properties.Add(role, (propertyName, propertyType));
                }
            }
        }

        _ = RequiredName(NestedSetPropertyRoles.TreeId);
        _ = RequiredName(NestedSetPropertyRoles.Left);
        _ = RequiredName(NestedSetPropertyRoles.Right);
        _ = RequiredName(NestedSetPropertyRoles.Depth);
        _ = RequiredName(NestedSetPropertyRoles.Parent);
        _ = RequiredName(NestedSetPropertyRoles.Position);

        var order = _order ?? ExistingOrder();
        var orderMode = _orderMode ?? ExistingOrderMode();

        if (order.Count == 0
            && (_orderMode is not null || entity.FindAnnotation(NestedSetAnnotationNames.OrderMode) is not null))
        {
            throw new InvalidOperationException("Configure sibling-order properties before selecting an order mode.");
        }

        var orderProperties = order
            .Select(criterion => criterion.Name)
            .ToArray();

        if (order.Count > 0)
        {
            // WHY: Validate all criteria against pending roles before applying any mapping or order annotations.
            var structuralProperties = new List<string>
            {
                RequiredName(NestedSetPropertyRoles.TreeId),
                RequiredName(NestedSetPropertyRoles.Left),
                RequiredName(NestedSetPropertyRoles.Right),
                RequiredName(NestedSetPropertyRoles.Depth),
                RequiredName(NestedSetPropertyRoles.Position),
                RequiredName(NestedSetPropertyRoles.Parent),
            };

            var nodeKey = OptionalName(NestedSetPropertyRoles.NodeKey)
                ?? (entity.FindPrimaryKey() is { Properties.Count: 1 } currentPrimaryKey
                    ? currentPrimaryKey.Properties[0].Name
                    : null);

            if (nodeKey is not null)
            {
                structuralProperties.Add(nodeKey);
            }

            if (OptionalName(NestedSetPropertyRoles.Scope) is { } scope)
            {
                structuralProperties.Add(scope);
            }

            NestedSetOrdering.ValidateProperties(
                entity,
                orderProperties,
                structuralProperties,
                order
                    .Select(criterion => criterion.NullSortOrder)
                    .ToArray(),
                false);
        }

        foreach (var (role, property) in _properties)
        {
            _builder.Property(property.Type, property.Name);
            entity.SetAnnotation(NestedSetAnnotationNames.Prefix + role, property.Name);
        }

        if (entity.FindAnnotation(NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.NodeKey) is null
            && entity.FindPrimaryKey() is { Properties.Count: 1 } primaryKey)
        {
            // WHY: The common scalar-primary-key case should not require redundant node-key configuration.
            entity.SetAnnotation(
                NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.NodeKey,
                primaryKey.Properties[0].Name);
        }

        if (_preserveApplicationConcurrencyTokens)
        {
            // WHY: Set-based structural writes cannot safely invent the next value for an application-owned token.
            entity.SetAnnotation(NestedSetAnnotationNames.PreserveApplicationConcurrencyTokens, true);
        }

        if (order.Count > 0)
        {
            // WHY: Snapshot simple annotation values so later builder calls cannot mutate the published metadata.
            entity.SetAnnotation(NestedSetAnnotationNames.OrderProperties, orderProperties);
            entity.SetAnnotation(
                NestedSetAnnotationNames.OrderDescending,
                order
                    .Select(criterion => criterion.Descending)
                    .ToArray());

            entity.SetAnnotation(
                NestedSetAnnotationNames.OrderNullSort,
                order
                    .Select(criterion => criterion.NullSortOrder is { } value ? (int)value : -1)
                    .ToArray());

            entity.SetAnnotation(NestedSetAnnotationNames.OrderMode, (int)orderMode);
        }

        // WHY: Add indexes before provider finalizers budget column lengths, even without opt-in conventions.
        NestedSetIndexes.Reconcile((IConventionEntityType)entity);
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
        var order = append ? _order ?? ExistingOrder() : [];

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
            .Select((
                name,
                index
            ) => (name, descending[index],
                nullSort[index] == -1 ? (NullSortOrder?)null : (NullSortOrder)nullSort[index]))
            .ToList();
    }

    /// <summary>Reads the previous placement policy without changing its stored representation.</summary>
    /// <returns>The configured policy, or Strict when no policy was configured.</returns>
    /// <exception cref="InvalidOperationException">The stored value is not a supported order mode.</exception>
    private NestedSetOrderMode ExistingOrderMode()
    {
        var value = _builder.Metadata.FindAnnotation(NestedSetAnnotationNames.OrderMode)
            ?.Value;

        return value is null
            ? NestedSetOrderMode.Strict
            : value is int mode && Enum.IsDefined((NestedSetOrderMode)mode)
                ? (NestedSetOrderMode)mode
                : throw new InvalidOperationException("The nested-set sibling order mode is invalid.");
    }

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
