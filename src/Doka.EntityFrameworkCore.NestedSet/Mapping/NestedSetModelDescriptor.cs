namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

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
    /// <param name="ancestorIndexProperty">The optional generated nonleaf scalar, separate from writable structure.</param>
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
        NestedSetOrderMode? orderMode,
        IReadOnlyProperty? ancestorIndexProperty = null
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
        AncestorIndexProperty = ancestorIndexProperty is null
            ? null
            : NestedSetPropertyDescriptor.Create(ancestorIndexProperty);

        OrderMode = orderMode;

        NestedSetPropertyDescriptor[] structuralProperties = Scope is { } scopeDescriptor
            ? [
                NodeKey,
                scopeDescriptor,
                TreeId,
                Parent,
                Left,
                Right,
                Depth,
                Position,
            ]
            : [
                NodeKey,
                TreeId,
                Parent,
                Left,
                Right,
                Depth,
                Position,
            ];

        StructuralProperties = Array.AsReadOnly(structuralProperties);
        var order = new NestedSetOrderPropertyDescriptor[orderProperties.Count];

        // WHY: Align each order property with its direction and null-placement vectors.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
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
        var ancestorIndex = NestedSetAncestorIndex.Name(entity) is { } flagName
            ? entity.FindProperty(flagName)
            ?? throw new InvalidOperationException(
                $"Nested-set ancestor-index property '{flagName}' on '{entity.Name}' is not mapped.")
            : null;

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
                null,
                ancestorIndex);
        }

        if (namesValue is not string[] { Length: > 0 } names
            || directionsValue is not bool[] directions
            || nullSortValue is not int[] nullSort
            || names.Length != directions.Length
            || names.Length != nullSort.Length
            || nullSort.Any(innerValue => innerValue != -1 && !Enum.IsDefined((NullSortOrder)innerValue)))
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
            .Select(innerValue => innerValue == -1 ? (NullSortOrder?)null : (NullSortOrder)innerValue)
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
            mode,
            ancestorIndex);
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

    /// <summary>Gets the optional generated nonleaf scalar without treating it as writable hierarchy structure.</summary>
    internal NestedSetPropertyDescriptor? AncestorIndexProperty { get; }

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
    ) => entity.FindAnnotation(NestedSetAnnotationNames.Prefix + role)?.Value is string name
        ? entity.FindProperty(name)
        : null;
}
