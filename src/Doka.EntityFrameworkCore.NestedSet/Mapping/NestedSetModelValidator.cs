namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Validates finalized hierarchy roles and exposes their index-relevant metadata.</summary>
internal static class NestedSetModelValidator
{
    /// <summary>Determines whether any structural role marks the entity as a nested-set node.</summary>
    /// <param name="entity">The entity metadata to inspect.</param>
    /// <returns>Whether nested-set structural configuration has been applied.</returns>
    internal static bool IsNestedSet(
        IReadOnlyEntityType entity
    ) => entity
        .GetAnnotations()
        .Any(annotation => annotation.Name.StartsWith(NestedSetAnnotationNames.Prefix, StringComparison.Ordinal)
            && NestedSetPropertyRoles.All.Contains(
                annotation.Name[NestedSetAnnotationNames.Prefix.Length..],
                StringComparer.Ordinal));

    /// <summary>
    /// Validates role types, identity, ordering, and relational storage without opening a connection.
    /// </summary>
    /// <param name="entity">The mutable convention entity metadata.</param>
    /// <param name="required">Whether incomplete metadata must throw rather than await later conventions.</param>
    /// <returns>The validated descriptor, or null while an incomplete mutable model can still be completed.</returns>
    /// <exception cref="InvalidOperationException">The completed nested-set model violates its contract.</exception>
    internal static NestedSetModelDescriptor? Validate(
        IConventionEntityType entity,
        bool required
    )
    {
        var nodeKey = ResolveNodeKey(entity, required);
        var treeId = Property(entity, NestedSetPropertyRoles.TreeId, required);
        var parent = Property(entity, NestedSetPropertyRoles.Parent, required);
        var left = Property(entity, NestedSetPropertyRoles.Left, required);
        var right = Property(entity, NestedSetPropertyRoles.Right, required);
        var depth = Property(entity, NestedSetPropertyRoles.Depth, required);
        var position = Property(entity, NestedSetPropertyRoles.Position, required);
        var scope = Property(entity, NestedSetPropertyRoles.Scope, false);
        var ancestorIndex = NestedSetAncestorIndex.Name(entity) is { } flagName
            ? entity.FindProperty(flagName)
            ?? (required
                ? throw new InvalidOperationException(
                    $"Nested-set ancestor-index property '{flagName}' on '{entity.Name}' is not mapped.")
                : null)
            : null;

        if (nodeKey is null
            || treeId is null
            || parent is null
            || left is null
            || right is null
            || depth is null
            || position is null)
        {
            return null;
        }

        IConventionProperty[] roles = scope is null
            ? [
                nodeKey,
                treeId,
                parent,
                left,
                right,
                depth,
                position,
            ]
            : [
                nodeKey,
                scope,
                treeId,
                parent,
                left,
                right,
                depth,
                position,
            ];

        var (orderProperties, orderDescending, orderNullSort, orderMode) = ValidateOrder(
            entity,
            nodeKey,
            roles,
            required);

        if (!required)
        {
            // WHY: Application keys, store mappings, and generation rules can still change after HasNestedSet runs.
            // Early reconciliation only needs the property paths; the finalizing convention enforces the contract.

            return new NestedSetModelDescriptor(
                nodeKey,
                scope,
                treeId,
                parent,
                left,
                right,
                depth,
                position,
                orderProperties,
                orderDescending,
                orderNullSort,
                orderMode,
                ancestorIndex);
        }

        if (roles.Distinct().Count() != roles.Length)
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires a different property for every structural role.");
        }

        ValidateIdentity(entity, nodeKey, scope);
        Required(nodeKey, entity, NestedSetPropertyRoles.NodeKey);
        Required(treeId, entity, NestedSetPropertyRoles.TreeId);
        Writable(treeId, entity, NestedSetPropertyRoles.TreeId);

        if (scope is not null)
        {
            Required(scope, entity, NestedSetPropertyRoles.Scope);
            AssignableOnInsert(scope, entity, NestedSetPropertyRoles.Scope);
        }

        if (!parent.IsNullable
            || ModelType(parent) != ModelType(nodeKey))
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires a nullable Parent property with the NodeKey model type.");
        }

        RequiredType(left, typeof(long), entity, NestedSetPropertyRoles.Left);
        RequiredType(right, typeof(long), entity, NestedSetPropertyRoles.Right);
        RequiredType(depth, typeof(int), entity, NestedSetPropertyRoles.Depth);
        RequiredType(position, typeof(long), entity, NestedSetPropertyRoles.Position);
        RequireUnconvertedArithmetic(left, entity, NestedSetPropertyRoles.Left);
        RequireUnconvertedArithmetic(right, entity, NestedSetPropertyRoles.Right);
        RequireUnconvertedArithmetic(depth, entity, NestedSetPropertyRoles.Depth);
        RequireUnconvertedArithmetic(position, entity, NestedSetPropertyRoles.Position);
        RequireCompatibleIdentity(nodeKey, parent, entity);

        foreach (var (property, role) in new[]
                 {
                     (parent, NestedSetPropertyRoles.Parent),
                     (left, NestedSetPropertyRoles.Left),
                     (right, NestedSetPropertyRoles.Right),
                     (depth, NestedSetPropertyRoles.Depth),
                     (position, NestedSetPropertyRoles.Position),
                 })
        {
            Writable(property, entity, role);
        }

        var descriptor = new NestedSetModelDescriptor(
            nodeKey,
            scope,
            treeId,
            parent,
            left,
            right,
            depth,
            position,
            orderProperties,
            orderDescending,
            orderNullSort,
            orderMode,
            ancestorIndex);

        if (entity.GetMappingStrategy() == RelationalAnnotationNames.TpcMappingStrategy
            && entity
                .GetDerivedTypes()
                .Any())
        {
            throw new InvalidOperationException(
                "A polymorphic TPC hierarchy spans multiple concrete tables. Configure each concrete node type "
                + "as an independent nested-set hierarchy.");
        }

        _ = NestedSetStoreObject.Resolve(entity, descriptor);

        ValidateApplicationConcurrencyPolicy(entity);

        return descriptor;
    }

    /// <summary>Requires an explicit preservation decision for tokens whose next value belongs to the application.</summary>
    /// <param name="entity">The finalized hierarchy entity.</param>
    /// <exception cref="InvalidOperationException">An application-managed token has no structural-write policy.</exception>
    private static void ValidateApplicationConcurrencyPolicy(
        IConventionEntityType entity
    )
    {
        var applicationTokens = FlattenedProperties(entity)
            .Where(property => property.IsConcurrencyToken && (property.ValueGenerated & ValueGenerated.OnUpdate) == 0)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (applicationTokens.Length == 0
            || entity.FindAnnotation(NestedSetAnnotationNames.PreserveApplicationConcurrencyTokens)
                ?.Value is true)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Nested-set entity '{entity.Name}' has application-managed concurrency token(s) "
            + $"'{string.Join("', '", applicationTokens)}'. Call "
            + $"{nameof(NestedSetBuilder<>.PreserveApplicationConcurrencyTokens)} to declare that "
            + "structure-only writes preserve their values, or configure the tokens as store-generated on update.");
    }

    /// <summary>Enumerates scalar properties, including nested complex members, before runtime metadata exists.</summary>
    /// <param name="type">The convention type whose properties are required.</param>
    /// <returns>All scalar properties reachable through non-collection complex properties.</returns>
    private static IEnumerable<IConventionProperty> FlattenedProperties(
        IConventionTypeBase type
    )
    {
        foreach (var property in type.GetProperties())
        {
            yield return property;
        }

        foreach (var complexProperty in type.GetComplexProperties())
        {
            foreach (var property in FlattenedProperties(complexProperty.ComplexType))
            {
                yield return property;
            }
        }
    }

    /// <summary>Resolves an explicit node key or adopts a scalar primary key by convention.</summary>
    /// <param name="entity">The configured entity.</param>
    /// <param name="required">Whether a missing identity must fail.</param>
    /// <returns>The scalar node-key property, or null while key conventions can still complete the model.</returns>
    private static IConventionProperty? ResolveNodeKey(
        IConventionEntityType entity,
        bool required
    )
    {
        var annotation = entity.FindAnnotation(NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.NodeKey);

        if (annotation?.Value is string configuredName)
        {
            var configured = entity.FindProperty(configuredName);
            if (configured is not null)
            {
                return configured;
            }

            if (required)
            {
                throw new InvalidOperationException(
                    $"Nested-set entity '{entity.Name}' requires a mapped NodeKey property.");
            }

            return null;
        }

        if (entity.FindPrimaryKey() is { Properties.Count: 1 } primaryKey)
        {
            var nodeKey = primaryKey.Properties[0];

            // WHY: Persisting the inferred role gives runtime and compiled-model consumers one metadata path.
            entity.SetAnnotation(NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.NodeKey, nodeKey.Name);

            return nodeKey;
        }

        if (required)
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' with a composite or missing primary key requires HasNodeKey "
                + "and a scalar alternate key.");
        }

        return null;
    }

    /// <summary>Resolves a role annotation and its mapped property.</summary>
    /// <param name="entity">The configured entity.</param>
    /// <param name="role">The structural role.</param>
    /// <param name="required">Whether absence is invalid at this model stage.</param>
    /// <returns>The mapped property, or null when the role is absent.</returns>
    private static IConventionProperty? Property(
        IConventionEntityType entity,
        string role,
        bool required
    )
    {
        var annotation = entity.FindAnnotation(NestedSetAnnotationNames.Prefix + role);
        var property = annotation?.Value is string name ? entity.FindProperty(name) : null;

        if (property is null && required)
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires a mapped {role} property.");
        }

        return property;
    }

    /// <summary>Requires a non-null scalar role.</summary>
    /// <param name="property">The property fulfilling the role.</param>
    /// <param name="entity">The declaring entity.</param>
    /// <param name="role">The structural role name.</param>
    private static void Required(
        IConventionProperty property,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.IsNullable)
        {
            throw new InvalidOperationException(
                $"Nested-set {role} property '{property.Name}' on '{entity.Name}' must be required.");
        }
    }

    /// <summary>Requires one exact non-null CLR storage type for an arithmetic structural role.</summary>
    /// <param name="property">The property fulfilling the role.</param>
    /// <param name="type">The required CLR type.</param>
    /// <param name="entity">The declaring entity.</param>
    /// <param name="role">The structural role name.</param>
    private static void RequiredType(
        IConventionProperty property,
        Type type,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.ClrType != type
            || property.IsNullable)
        {
            throw new InvalidOperationException(
                $"Nested-set {role} property '{property.Name}' on '{entity.Name}' must be required {type.Name}.");
        }
    }

    /// <summary>Rejects provider conversions that would change numeric SQL arithmetic semantics.</summary>
    private static void RequireUnconvertedArithmetic(
        IConventionProperty property,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.GetValueConverter() is not null
            || property.GetProviderClrType() is not null)
        {
            throw new InvalidOperationException(
                $"Nested-set arithmetic property '{property.Name}' for {role} on '{entity.Name}' "
                + "must use its native numeric store representation.");
        }
    }

    /// <summary>Requires NodeKey and Parent to retain the same relational equality representation.</summary>
    private static void RequireCompatibleIdentity(
        IConventionProperty nodeKey,
        IConventionProperty parent,
        IConventionEntityType entity
    )
    {
        var keyProvider = ProviderType(nodeKey);
        var parentProvider = ProviderType(parent);

        if (keyProvider != parentProvider
            || nodeKey.GetColumnType() != parent.GetColumnType()
            || nodeKey.GetMaxLength() != parent.GetMaxLength()
            || nodeKey.IsUnicode() != parent.IsUnicode()
            || nodeKey.IsFixedLength() != parent.IsFixedLength()
            || nodeKey.GetPrecision() != parent.GetPrecision()
            || nodeKey.GetScale() != parent.GetScale())
        {
            throw new InvalidOperationException(
                $"Nested-set NodeKey '{nodeKey.Name}' and Parent '{parent.Name}' on '{entity.Name}' "
                + "must use compatible relational mappings.");
        }
    }

    /// <summary>Returns the non-null provider type configured before relational type mapping is initialized.</summary>
    private static Type ProviderType(
        IConventionProperty property
    )
    {
        var converter = property.GetValueConverter();
        var provider = property.GetProviderClrType() ?? converter?.ProviderClrType ?? property.ClrType;

        return Nullable.GetUnderlyingType(provider) ?? provider;
    }

    /// <summary>Requires a hierarchy-owned role to remain writable throughout the entity lifetime.</summary>
    /// <param name="property">The property fulfilling the role.</param>
    /// <param name="entity">The declaring entity.</param>
    /// <param name="role">The structural role name.</param>
    private static void Writable(
        IConventionProperty property,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.ValueGenerated != ValueGenerated.Never
            || property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save
            || property.GetAfterSaveBehavior() != PropertySaveBehavior.Save)
        {
            throw new InvalidOperationException(
                $"Nested-set structural property '{property.Name}' for {role} on '{entity.Name}' must be writable "
                + "and not generated.");
        }
    }

    /// <summary>Requires an immutable partition identity to accept an application value during insertion.</summary>
    /// <param name="property">The property fulfilling the role.</param>
    /// <param name="entity">The declaring entity.</param>
    /// <param name="role">The structural role name.</param>
    private static void AssignableOnInsert(
        IConventionProperty property,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.ValueGenerated != ValueGenerated.Never
            || property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save)
        {
            throw new InvalidOperationException(
                $"Nested-set structural property '{property.Name}' for {role} on '{entity.Name}' must accept an "
                + "assigned value and not be generated.");
        }

        // WHY: Scope is an immutable partition identity and commonly belongs to an EF key. EF therefore uses Throw
        // after insertion, while nested-set operations only need to assign it before the initial save.
    }

    /// <summary>Requires the node key to be protected by a scalar or scope-qualified EF key.</summary>
    /// <param name="entity">The configured entity.</param>
    /// <param name="nodeKey">The selected scalar node key.</param>
    /// <param name="scope">The optional scope property.</param>
    private static void ValidateIdentity(
        IConventionEntityType entity,
        IConventionProperty nodeKey,
        IConventionProperty? scope
    )
    {
        var hasScalarIdentity = entity
            .GetKeys()
            .Any(key => key.Properties.Count == 1 && key.Properties[0] == nodeKey);

        var hasScopedIdentity = scope is not null
            && entity
                .GetKeys()
                .Any(key => key.Properties.Count == 2 && key.Properties[0] == scope && key.Properties[1] == nodeKey);

        if (!hasScalarIdentity
            && !hasScopedIdentity)
        {
            var expected = scope is null ? "NodeKey" : "NodeKey or (Scope, NodeKey)";

            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires an EF primary or alternate key over {expected}.");
        }
    }

    /// <summary>
    /// Validates order metadata and materializes index-key properties without changing application metadata.
    /// </summary>
    /// <param name="entity">The configured entity.</param>
    /// <param name="nodeKey">The deterministic tie-breaker.</param>
    /// <param name="structuralProperties">Every property reserved for hierarchy structure.</param>
    /// <param name="validateNullability">Whether EF has finalized each property's required/optional state.</param>
    /// <returns>The order properties with their exact direction and null-placement vectors.</returns>
    private static ( IConventionProperty[] Properties, bool[] Descending, NullSortOrder?[] NullSortOrders,
        NestedSetOrderMode? Mode) ValidateOrder(
            IConventionEntityType entity,
            IConventionProperty nodeKey,
            IReadOnlyList<IConventionProperty> structuralProperties,
            bool validateNullability
        )
    {
        var namesValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderProperties)?.Value;
        var descendingValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderDescending)?.Value;
        var nullSortValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderNullSort)?.Value;
        var modeValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderMode)?.Value;

        if (namesValue is null
            && descendingValue is null
            && nullSortValue is null
            && modeValue is null)
        {
            return ([], [], [], null);
        }

        if (namesValue is not string[] { Length: > 0 } names
            || descendingValue is not bool[] descending
            || nullSortValue is not int[] nullSort
            || names.Length != descending.Length
            || names.Length != nullSort.Length
            || nullSort.Any(innerValue => innerValue != -1 && !Enum.IsDefined((NullSortOrder)innerValue)))
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires matching sibling-order property, direction, and null-order arrays.");
        }

        if (modeValue is not null
            && (modeValue is not int mode || !Enum.IsDefined((NestedSetOrderMode)mode)))
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' has an invalid sibling-order mode.");
        }

        var structuralNames = structuralProperties
            .Select(property => property.Name)
            .ToArray();

        var nullSortOrders = nullSort
            .Select(innerValue => innerValue == -1 ? (NullSortOrder?)null : (NullSortOrder)innerValue)
            .ToArray();

        NestedSetOrdering.ValidateProperties(entity, names, structuralNames, nullSortOrders, validateNullability);

        if (names.Contains(nodeKey.Name, StringComparer.Ordinal))
        {
            // WHY: NodeKey is always appended ascending, so allowing it in domain criteria could reverse tie order.
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' cannot configure NodeKey as a sibling-order criterion.");
        }

        var properties = names
            .Select(name => entity.FindProperty(name)
                ?? throw new InvalidOperationException(
                    $"Nested-set sibling-order property '{name}' on '{entity.Name}' is not mapped."))
            .ToArray();

        var orderMode = modeValue is int value ? (NestedSetOrderMode)value : NestedSetOrderMode.Strict;

        return (properties, descending.ToArray(), nullSortOrders, orderMode);
    }

    /// <summary>Returns the provider-comparison model type after applying any value converter.</summary>
    /// <param name="property">The property whose model type participates in role compatibility.</param>
    /// <returns>The non-nullable CLR model type.</returns>
    private static Type ModelType(
        IConventionProperty property
    ) => Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
}
