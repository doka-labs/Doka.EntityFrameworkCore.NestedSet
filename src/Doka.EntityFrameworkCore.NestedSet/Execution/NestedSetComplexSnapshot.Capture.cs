namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    /// <summary>Captures containers in parent-first order, including null boundaries and fixed-size lists.</summary>
    private void CaptureMembers(
        ITypeBase type,
        object instance,
        int[] ordinals
    )
    {
        foreach (var property in type.GetComplexProperties())
        {
            var value = property
                .GetGetter()
                .GetClrValue(instance);

            _members.Add(new MemberSnapshot(property, value, ordinals));

            if (!property.IsCollection)
            {
                if (value is not null)
                {
                    CaptureMembers(property.ComplexType, value, ordinals);
                }

                continue;
            }

            if (value is not IList list
                || !property
                    .ComplexType
                    .GetComplexProperties()
                    .Any())
            {
                continue;
            }

            // WHY: Captured ordinal paths let EF's mapped setter copy nested value-type references back
            // through their containers; assigning a boxed struct's reflected member would lose that write.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < list.Count; index++)
            {
                if (list[index] is null)
                {
                    continue;
                }

                var path = new int[ordinals.Length + 1];
                ordinals.CopyTo(path, 0);
                path[^1] = index;
                CaptureMembers(property.ComplexType, list[index]!, path);
            }
        }
    }

    /// <summary>Includes original-only Deleted entries and current slots beyond a raw shortened CLR list.</summary>
    private void CaptureCollections(
        object owner,
        ITypeBase type,
        object? instance,
        int[] path
    )
    {
        foreach (var property in type.GetFlattenedComplexProperties())
        {
            if (!property.IsCollection)
            {
                continue;
            }

            var container = DeclaringInstance(type, instance, property.DeclaringType);
            var value = container is null
                ? null
                : property
                    .GetGetter()
                    .GetClrValue(container);

            var snapshot = new CollectionSnapshot(owner, property, value as IList, path);
            _collections.Add(snapshot);

            if (!property
                .ComplexType
                .GetFlattenedComplexProperties()
                .Any(nested => nested.IsCollection))
            {
                continue;
            }

            foreach (var element in snapshot.Elements)
            {
                // WHY: Native element ownership is acyclic and unique to this collection. Its two lanes
                // are deduplicated by the frame, so no graph-sized visited set is needed during capture.
                var childPath = new int[path.Length + 1];
                path.CopyTo(childPath, 0);
                childPath[^1] = element.CurrentSlot;
                CaptureCollections(
                    element.Native,
                    property.ComplexType,
                    snapshot.CurrentItem(element.CurrentSlot),
                    childPath);
            }
        }
    }

    /// <summary>Reads a flattened reference without accessing a missing current ordinal.</summary>
    private static object? DeclaringInstance(
        ITypeBase ownerType,
        object? instance,
        ITypeBase declaringType
    )
    {
        if (instance is null
            || ownerType == declaringType
            || declaringType is not IComplexType complex)
        {
            return instance;
        }

        var property = complex.ComplexProperty;
        var parent = DeclaringInstance(ownerType, instance, property.DeclaringType);

        return parent is null
            ? null
            : property
                .GetGetter()
                .GetClrValue(parent);
    }

    /// <summary>Reads a potentially absent callback path without invoking EF's throwing ordinal accessor.</summary>
    private static object? ReadContaining(
        ITypeBase type,
        object root,
        int[] path,
        ref int depth,
        List<(IComplexProperty Property, object Instance)>? references = null
    )
    {
        if (type is not IComplexType complex)
        {
            return root;
        }

        var property = complex.ComplexProperty;
        var parent = ReadContaining(property.DeclaringType, root, path, ref depth, references);

        if (parent is null)
        {
            return null;
        }

        var value = property
            .GetGetter()
            .GetClrValue(parent);

        if (!property.IsCollection)
        {
            if (value is not null)
            {
                references?.Add((property, value));
            }

            return value;
        }

        var ordinal = depth < path.Length
            ? path[depth++]
            : -1;

        return value is IList list && (uint)ordinal < (uint)list.Count
            ? list[ordinal]
            : null;
    }

    /// <summary>Reads both native lanes before state callbacks can mutate either list.</summary>
    private static object[] ReadElements(
        object owner,
        IComplexProperty property
    )
    {
        var current = NestedSetComplexEntryAccess.Entries(owner, property, false);
        var original = NestedSetComplexEntryAccess.Entries(owner, property, true);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var elements = new List<object>(current.Count + original.Count);

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var lane in new[] { current, original })
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var element in lane)
            {
                if (element is not null
                    && seen.Add(element))
                {
                    elements.Add(element);
                }
            }
        }

        return elements.ToArray();
    }
}
