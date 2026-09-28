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

        var roles = scope is null
            ? new[]
            {
                nodeKey,
                treeId,
                parent,
                left,
                right,
                depth,
                position,
            }
            : new[]
            {
                nodeKey,
                scope,
                treeId,
                parent,
                left,
                right,
                depth,
                position,
            };

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
                orderMode);
        }

        if (roles
                .Distinct()
                .Count()
            != roles.Length)
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
            orderMode);

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
            || nullSort.Any(value => value != -1 && !Enum.IsDefined((NullSortOrder)value)))
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
            .Select(value => value == -1 ? (NullSortOrder?)null : (NullSortOrder)value)
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

/// <summary>Holds one immutable copy of the validated nested-set role and ordering contract.</summary>
internal sealed class NestedSetModelDescriptor
{
    /// <summary>Creates a descriptor without retaining mutable EF convention metadata or annotation arrays.</summary>
    /// <param name="nodeKey">The stable hierarchy identity.</param>
    /// <param name="scope">The optional application partition.</param>
    /// <param name="treeId">The stable tree identity.</param>
    /// <param name="parent">The nullable direct-parent identity.</param>
    /// <param name="left">The Int64 left boundary.</param>
    /// <param name="right">The Int64 right boundary.</param>
    /// <param name="depth">The Int32 node depth.</param>
    /// <param name="position">The Int64 sibling position.</param>
    /// <param name="orderProperties">The configured domain sort properties.</param>
    /// <param name="orderDescending">The direction matching every domain sort property.</param>
    /// <param name="orderNullSort">The explicit null placement matching every domain sort property.</param>
    /// <param name="orderMode">The configured automatic sibling-order policy.</param>
    internal NestedSetModelDescriptor(
        IReadOnlyProperty nodeKey,
        IReadOnlyProperty? scope,
        IReadOnlyProperty treeId,
        IReadOnlyProperty parent,
        IReadOnlyProperty left,
        IReadOnlyProperty right,
        IReadOnlyProperty depth,
        IReadOnlyProperty position,
        IReadOnlyList<IReadOnlyProperty> orderProperties,
        IReadOnlyList<bool> orderDescending,
        IReadOnlyList<NullSortOrder?> orderNullSort,
        NestedSetOrderMode? orderMode
    )
    {
        NodeKey = NestedSetPropertyDescriptor.Create(nodeKey);
        Scope = scope is null ? null : NestedSetPropertyDescriptor.Create(scope);
        TreeId = NestedSetPropertyDescriptor.Create(treeId);
        Parent = NestedSetPropertyDescriptor.Create(parent);
        Left = NestedSetPropertyDescriptor.Create(left);
        Right = NestedSetPropertyDescriptor.Create(right);
        Depth = NestedSetPropertyDescriptor.Create(depth);
        Position = NestedSetPropertyDescriptor.Create(position);
        OrderMode = orderMode;

        var structuralProperties = Scope is { } scopeDescriptor
            ? new[]
            {
                NodeKey,
                scopeDescriptor,
                TreeId,
                Parent,
                Left,
                Right,
                Depth,
                Position,
            }
            : new[]
            {
                NodeKey,
                TreeId,
                Parent,
                Left,
                Right,
                Depth,
                Position,
            };

        StructuralProperties = Array.AsReadOnly(structuralProperties);
        var order = new NestedSetOrderPropertyDescriptor[orderProperties.Count];

        for (var index = 0; index < order.Length; index++)
        {
            order[index] = new NestedSetOrderPropertyDescriptor(
                NestedSetPropertyDescriptor.Create(orderProperties[index]),
                orderDescending[index],
                orderNullSort[index]);
        }

        Order = Array.AsReadOnly(order);
    }

    /// <summary>Creates the immutable runtime descriptor from already validated finalized metadata.</summary>
    /// <param name="entity">The finalized nested-set entity.</param>
    /// <returns>A copied descriptor that does not retain annotation arrays.</returns>
    internal static NestedSetModelDescriptor FromFinalized(
        IReadOnlyEntityType entity
    )
    {
        var nodeKey = RequiredProperty(entity, NestedSetPropertyRoles.NodeKey);
        var scope = OptionalProperty(entity, NestedSetPropertyRoles.Scope);
        var treeId = RequiredProperty(entity, NestedSetPropertyRoles.TreeId);
        var parent = RequiredProperty(entity, NestedSetPropertyRoles.Parent);
        var left = RequiredProperty(entity, NestedSetPropertyRoles.Left);
        var right = RequiredProperty(entity, NestedSetPropertyRoles.Right);
        var depth = RequiredProperty(entity, NestedSetPropertyRoles.Depth);
        var position = RequiredProperty(entity, NestedSetPropertyRoles.Position);
        var namesValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderProperties)?.Value;
        var directionsValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderDescending)?.Value;
        var nullSortValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderNullSort)?.Value;
        var modeValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderMode)?.Value;

        if (namesValue is null
            && directionsValue is null
            && nullSortValue is null
            && modeValue is null)
        {
            return new NestedSetModelDescriptor(
                nodeKey,
                scope,
                treeId,
                parent,
                left,
                right,
                depth,
                position,
                [],
                [],
                [],
                null);
        }

        if (namesValue is not string[] { Length: > 0 } names
            || directionsValue is not bool[] directions
            || nullSortValue is not int[] nullSort
            || names.Length != directions.Length
            || names.Length != nullSort.Length
            || nullSort.Any(value => value != -1 && !Enum.IsDefined((NullSortOrder)value)))
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' has invalid finalized sibling-order metadata.");
        }

        var mode = modeValue is null
            ? NestedSetOrderMode.Strict
            : modeValue is int value && Enum.IsDefined((NestedSetOrderMode)value)
                ? (NestedSetOrderMode)value
                : throw new InvalidOperationException(
                    $"Nested-set entity '{entity.Name}' has an invalid finalized sibling-order mode.");

        var orderProperties = names
            .Select(name => entity.FindProperty(name)
                ?? throw new InvalidOperationException(
                    $"Nested-set sibling-order property '{name}' on '{entity.Name}' is not mapped."))
            .ToArray();

        var nullSortOrders = nullSort
            .Select(value => value == -1 ? (NullSortOrder?)null : (NullSortOrder)value)
            .ToArray();

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
            directions,
            nullSortOrders,
            mode);
    }

    /// <summary>Gets the stable hierarchy identity.</summary>
    internal NestedSetPropertyDescriptor NodeKey { get; }

    /// <summary>Gets the optional application partition.</summary>
    internal NestedSetPropertyDescriptor? Scope { get; }

    /// <summary>Gets the stable tree identity.</summary>
    internal NestedSetPropertyDescriptor TreeId { get; }

    /// <summary>Gets the nullable direct-parent identity.</summary>
    internal NestedSetPropertyDescriptor Parent { get; }

    /// <summary>Gets the Int64 left boundary.</summary>
    internal NestedSetPropertyDescriptor Left { get; }

    /// <summary>Gets the Int64 right boundary.</summary>
    internal NestedSetPropertyDescriptor Right { get; }

    /// <summary>Gets the Int32 node depth.</summary>
    internal NestedSetPropertyDescriptor Depth { get; }

    /// <summary>Gets the Int64 sibling position.</summary>
    internal NestedSetPropertyDescriptor Position { get; }

    /// <summary>Gets every structural property, with Scope present only for scoped entities.</summary>
    internal IReadOnlyList<NestedSetPropertyDescriptor> StructuralProperties { get; }

    /// <summary>Gets copied domain sort properties and their directions.</summary>
    internal IReadOnlyList<NestedSetOrderPropertyDescriptor> Order { get; }

    /// <summary>Gets the configured order policy, or null for explicitly positioned siblings.</summary>
    internal NestedSetOrderMode? OrderMode { get; }

    /// <summary>Resolves one required finalized role property.</summary>
    private static IReadOnlyProperty RequiredProperty(
        IReadOnlyEntityType entity,
        string role
    ) => OptionalProperty(entity, role)
        ?? throw new InvalidOperationException($"Nested-set entity '{entity.Name}' requires a mapped {role} property.");

    /// <summary>Resolves one optional finalized role property.</summary>
    private static IReadOnlyProperty? OptionalProperty(
        IReadOnlyEntityType entity,
        string role
    ) => entity.FindAnnotation(NestedSetAnnotationNames.Prefix + role)
        ?.Value is string name
        ? entity.FindProperty(name)
        : null;
}

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

/// <summary>Pairs one immutable sibling-order property with its exact direction.</summary>
/// <param name="Property">The copied property identity and scalar facets.</param>
/// <param name="Descending">Whether the property sorts descending.</param>
/// <param name="NullSortOrder">The explicit null placement, or null for a required property.</param>
internal readonly record struct NestedSetOrderPropertyDescriptor(
    NestedSetPropertyDescriptor Property,
    bool Descending,
    NullSortOrder? NullSortOrder
);
