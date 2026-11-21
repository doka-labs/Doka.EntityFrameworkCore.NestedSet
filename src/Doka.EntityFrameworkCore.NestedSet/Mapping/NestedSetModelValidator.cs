namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Validates finalized hierarchy roles and exposes their index-relevant metadata.</summary>
internal static partial class NestedSetModelValidator
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
            ? [nodeKey, treeId, parent, left, right, depth, position]
            : [nodeKey, scope, treeId, parent, left, right, depth, position];

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

        RequireDistinctRoles(entity, roles);
        ValidateIdentityRoles(entity, nodeKey, scope, treeId, parent);
        ValidateArithmeticRoles(entity, left, right, depth, position);
        RequireCompatibleIdentity(nodeKey, parent, entity);
        ValidateRoleWrites(entity, parent, left, right, depth, position);

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

    /// <summary>
    /// Requires an explicit preservation decision for tokens whose next value belongs to the application.
    /// </summary>
    /// <param name="entity">The finalized hierarchy entity.</param>
    /// <exception cref="InvalidOperationException">
    /// An application-managed token has no structural-write policy.
    /// </exception>
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

    /// <summary>
    /// Enumerates scalar properties, including nested complex members, before runtime metadata exists.
    /// </summary>
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
        var namesValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderProperties)
            ?.Value;

        var descendingValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderDescending)
            ?.Value;

        var nullSortValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderNullSort)
            ?.Value;

        var modeValue = entity.FindAnnotation(NestedSetAnnotationNames.OrderMode)
            ?.Value;

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
                $"Nested-set entity '{entity.Name}' requires matching sibling-order property, direction, "
                + "and null-order arrays.");
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
            .Select(innerValue => innerValue == -1
                ? (NullSortOrder?)null
                : (NullSortOrder)innerValue)
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

        var orderMode = modeValue is int value
            ? (NestedSetOrderMode)value
            : NestedSetOrderMode.Strict;

        return (properties, descending.ToArray(), nullSortOrders, orderMode);
    }
}
