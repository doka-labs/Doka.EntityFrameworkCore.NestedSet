namespace Doka.EntityFrameworkCore.NestedSet;

public sealed partial class NestedSetBuilder<TEntity>
    where TEntity : class
{
    /// <summary>Applies validated role selections and the indexes used by structural queries.</summary>
    /// <exception cref="InvalidOperationException">
    ///     A required role has neither a selection nor an existing annotation.
    /// </exception>
    internal void Complete()
    {
        InferInterfaceRoles();
        RequireStructuralRoles();
        var order = _order ?? ExistingOrder();
        var orderMode = _orderMode ?? ExistingOrderMode();
        var orderProperties = order.Select(criterion => criterion.Name).ToArray();

        // WHY: Validate every pending selection before publishing any mapping or ordering annotation.
        ValidatePendingOrder(order, orderProperties);
        ApplyProperties();
        ApplyOrder(order, orderProperties, orderMode);
        CompleteAncestorIndex();
        CompleteTreeRegistry();

        // WHY: Add indexes before provider finalizers budget column lengths, even without opt-in conventions.
        var entity = (IConventionEntityType)_builder.Metadata;
        NestedSetIndexes.Reconcile(entity);
        NestedSetAncestorIndex.RemoveObsoleteProperties(entity);
    }

    /// <summary>Infers only unselected roles from the most specific supported node interface.</summary>
    private void InferInterfaceRoles()
    {
        var interfaces = typeof(TEntity).GetInterfaces();
        var scoped = interfaces.FirstOrDefault(type => type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(IScopedNestedSetNode<,,>));

        // WHY: Scoped nodes inherit the unscoped interface; prefer their complete tree identity.
        var hierarchy = scoped ?? interfaces.FirstOrDefault(type => type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(INestedSetNode<,>));

        if (hierarchy is null)
        {
            return;
        }

        (string Role, string Name)[] roles =
        [
            (NestedSetPropertyRoles.NodeKey, nameof(INestedSetNode<,>.Id)),
            (NestedSetPropertyRoles.TreeId, nameof(INestedSetNode<,>.TreeId)),
            (NestedSetPropertyRoles.Left, nameof(INestedSetNode<,>.Left)),
            (NestedSetPropertyRoles.Right, nameof(INestedSetNode<,>.Right)),
            (NestedSetPropertyRoles.Depth, nameof(INestedSetNode<,>.Depth)),
            (NestedSetPropertyRoles.Position, nameof(INestedSetNode<,>.Position)),
        ];

        foreach (var (role, name) in roles)
        {
            InferRole(role, name);
        }

        if (scoped is not null)
        {
            InferRole(NestedSetPropertyRoles.Scope, nameof(IScopedNestedSetNode<object, object, object>.Scope));
        }
    }

    /// <summary>Retains an explicit role or reads its interface-backed CLR property type.</summary>
    private void InferRole(
        string role,
        string propertyName
    )
    {
        if (_properties.ContainsKey(role)
            || _builder.Metadata.FindAnnotation(NestedSetAnnotationNames.Prefix + role) is not null)
        {
            return;
        }

        var propertyType = typeof(TEntity).GetProperty(propertyName)?.PropertyType
            ?? throw new InvalidOperationException(
                $"Nested-set interface property '{propertyName}' is not implemented as a CLR property.");

        _properties.Add(role, (propertyName, propertyType));
    }

    /// <summary>Rejects incomplete required roles before publishing pending metadata.</summary>
    private void RequireStructuralRoles()
    {
        _ = RequiredName(NestedSetPropertyRoles.TreeId);
        _ = RequiredName(NestedSetPropertyRoles.Left);
        _ = RequiredName(NestedSetPropertyRoles.Right);
        _ = RequiredName(NestedSetPropertyRoles.Depth);
        _ = RequiredName(NestedSetPropertyRoles.Parent);
        _ = RequiredName(NestedSetPropertyRoles.Position);
    }

    /// <summary>Checks sibling criteria against pending and already configured structural selections.</summary>
    private void ValidatePendingOrder(
        List<(string Name, bool Descending, NullSortOrder? NullSortOrder)> order,
        string[] orderProperties
    )
    {
        var entity = _builder.Metadata;

        if (order.Count == 0)
        {
            if (_orderMode is not null || entity.FindAnnotation(NestedSetAnnotationNames.OrderMode) is not null)
            {
                throw new InvalidOperationException(
                    "Configure sibling-order properties before selecting an order mode.");
            }

            return;
        }

        List<string> structuralProperties =
        [
            RequiredName(NestedSetPropertyRoles.TreeId),
            RequiredName(NestedSetPropertyRoles.Left),
            RequiredName(NestedSetPropertyRoles.Right),
            RequiredName(NestedSetPropertyRoles.Depth),
            RequiredName(NestedSetPropertyRoles.Position),
            RequiredName(NestedSetPropertyRoles.Parent),
        ];

        var nodeKey = OptionalName(NestedSetPropertyRoles.NodeKey)
            ?? (entity.FindPrimaryKey() is { Properties.Count: 1 } primaryKey ? primaryKey.Properties[0].Name : null);

        if (nodeKey is not null)
        {
            structuralProperties.Add(nodeKey);
        }

        if (OptionalName(NestedSetPropertyRoles.Scope) is { } scope)
        {
            structuralProperties.Add(scope);
        }

        NestedSetOrdering.ValidateProperties(
            entity, orderProperties, structuralProperties,
            order.Select(criterion => criterion.NullSortOrder).ToArray(), false);
    }

    /// <summary>Publishes role annotations only after all pending selections have passed validation.</summary>
    private void ApplyProperties()
    {
        var entity = _builder.Metadata;

        foreach (var (role, property) in _properties)
        {
            _builder.Property(property.Type, property.Name);
            entity.SetAnnotation(NestedSetAnnotationNames.Prefix + role, property.Name);
        }

        if (entity.FindAnnotation(NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.NodeKey) is null
            && entity.FindPrimaryKey() is { Properties.Count: 1 } primaryKey)
        {
            // WHY: The common scalar-primary-key case needs no redundant node-key configuration.
            entity.SetAnnotation(
                NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.NodeKey, primaryKey.Properties[0].Name);
        }

        if (_preserveApplicationConcurrencyTokens)
        {
            // WHY: Structural set writes cannot invent the next application-owned concurrency token.
            entity.SetAnnotation(NestedSetAnnotationNames.PreserveApplicationConcurrencyTokens, true);
        }
    }

    /// <summary>Snapshots the selected ordering so later builder calls cannot mutate published arrays.</summary>
    private void ApplyOrder(
        List<(string Name, bool Descending, NullSortOrder? NullSortOrder)> order,
        string[] orderProperties,
        NestedSetOrderMode orderMode
    )
    {
        if (order.Count == 0)
        {
            return;
        }

        var entity = _builder.Metadata;
        entity.SetAnnotation(NestedSetAnnotationNames.OrderProperties, orderProperties);
        entity.SetAnnotation(NestedSetAnnotationNames.OrderDescending, order.Select(criterion => criterion.Descending)
            .ToArray());
        entity.SetAnnotation(NestedSetAnnotationNames.OrderNullSort, order.Select(criterion =>
            criterion.NullSortOrder is { } value ? (int)value : -1).ToArray());
        entity.SetAnnotation(NestedSetAnnotationNames.OrderMode, (int)orderMode);
    }
}
