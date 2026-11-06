using System.Collections;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Reads full cached lane lengths and slots without initializing or repairing them.</summary>
    private static ComplexCompositionNativeState CaptureComplexCompositionNative(
        ComplexCallbackGraph graph
    )
    {
        var owners = new List<ComplexCompositionOwnerState>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var root = ComplexCompositionNativeAccess.Instance(graph.Row.Property(value => value.Label));
        CaptureComplexCompositionOwner(root, graph.Row.Metadata, "Row", owners, seen);

        return new ComplexCompositionNativeState(owners.ToArray());
    }

    /// <summary>Visits each captured native identity once while retaining both lanes' exact slot sequences.</summary>
    private static void CaptureComplexCompositionOwner(
        object native,
        ITypeBase type,
        string path,
        List<ComplexCompositionOwnerState> owners,
        HashSet<object> seen
    )
    {
        if (!seen.Add(native))
        {
            return;
        }

        var leaves = type
            .GetFlattenedProperties()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => ComplexCompositionNativeAccess.Property(native, property))
            .Select(property => new ComplexPeerLeafState(
                property.Metadata.Name,
                property.CurrentValue,
                property.OriginalValue,
                property.IsModified))
            .ToArray();

        var properties = ComplexCompositionCollections(type)
            .ToArray();

        var lanes = properties
            .Select(property => ComplexCompositionNativeAccess.Lane(native, property))
            .ToArray();

        owners.Add(
            new ComplexCompositionOwnerState(
                path,
                native,
                ComplexCompositionNativeAccess.State(native),
                ComplexCompositionNativeAccess.Ordinal(native, original: false),
                ComplexCompositionNativeAccess.Ordinal(native, original: true),
                leaves,
                lanes));

        // WHY: Cached slots may exceed CLR counts after staging. Read every slot at its physical ordinal,
        // including nulls, so a trailing hole cannot disappear from the recovery oracle.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            var lane = lanes[index];
            CaptureComplexCompositionChildren(lane.Current, "current");
            CaptureComplexCompositionChildren(lane.Original, "original");

            void CaptureComplexCompositionChildren(
                object?[]? slots,
                string role
            )
            {
                if (slots is null)
                {
                    return;
                }

                // WHY: Physical ordinal is part of the native-lane contract, including vacant slots.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var ordinal = 0; ordinal < slots.Length; ordinal++)
                {
                    if (slots[ordinal] is { } child)
                    {
                        CaptureComplexCompositionOwner(
                            child,
                            property.ComplexType,
                            path
                            + "."
                            + property.Name
                            + "."
                            + role
                            + "["
                            + ordinal.ToString(CultureInfo.InvariantCulture)
                            + "]",
                            owners,
                            seen);
                    }
                }
            }
        }
    }

    /// <summary>Includes collections flattened through reference complex properties under one native owner.</summary>
    private static IEnumerable<IComplexProperty> ComplexCompositionCollections(
        ITypeBase type
    )
    {
        foreach (var property in type.GetComplexProperties())
        {
            if (property.IsCollection)
            {
                yield return property;
            }
            else
            {
                foreach (var nested in ComplexCompositionCollections(property.ComplexType))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>Compares identity, lane lengths, holes, ordinals and values before retry can normalize EF.</summary>
    private static void AssertComplexCompositionNative(
        ComplexCompositionNativeState before,
        ComplexCompositionNativeState after
    )
    {
        Assert.Equal(before.Owners.Length, after.Owners.Length);

        // WHY: Identity and physical lane order must match, independently of record or sequence value equality.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Owners.Length; index++)
        {
            var expected = before.Owners[index];
            var actual = after.Owners[index];
            Assert.Equal(expected.Path, actual.Path);
            Assert.Same(expected.Native, actual.Native);
            Assert.Equal(expected.State, actual.State);
            Assert.Equal(expected.Ordinal, actual.Ordinal);
            Assert.Equal(expected.OriginalOrdinal, actual.OriginalOrdinal);
            Assert.Equal(expected.Leaves, actual.Leaves);
            Assert.Equal(expected.Lanes.Length, actual.Lanes.Length);

            // WHY: The mapped property index pairs each captured current/original lane with its restored lane.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var lane = 0; lane < expected.Lanes.Length; lane++)
            {
                Assert.Equal(expected.Lanes[lane].Name, actual.Lanes[lane].Name);
                Assert.Equal(expected.Lanes[lane].Modified, actual.Lanes[lane].Modified);
                AssertComplexCompositionSlots(expected.Lanes[lane].Current, actual.Lanes[lane].Current);
                AssertComplexCompositionSlots(expected.Lanes[lane].Original, actual.Lanes[lane].Original);
                AssertComplexCompositionSlots(
                    expected.Lanes[lane].OriginalValues,
                    actual.Lanes[lane].OriginalValues);
            }
        }
    }

    /// <summary>Distinguishes absent lanes from empty lanes and detects changed lengths or introduced holes.</summary>
    private static void AssertComplexCompositionSlots(
        object?[]? before,
        object?[]? after
    )
    {
        Assert.Equal(before?.Length, after?.Length);

        if (before is null)
        {
            return;
        }

        Assert.NotNull(after);

        // WHY: Native entries and original CLR members use reference identity; null slot positions also matter.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Length; index++)
        {
            Assert.Same(before[index], after[index]);
        }
    }

    /// <summary>Copies native entry state without retaining mutable lane aliases.</summary>
    private sealed record ComplexCompositionNativeState(ComplexCompositionOwnerState[] Owners);

    /// <summary>Retains one EF native identity with its scalar and collection snapshots.</summary>
    private sealed record ComplexCompositionOwnerState(
        string Path,
        object Native,
        EntityState State,
        int? Ordinal,
        int? OriginalOrdinal,
        ComplexPeerLeafState[] Leaves,
        ComplexCompositionLaneState[] Lanes
    );

    /// <summary>Copies physical lanes and original collection members, including vacant native slots.</summary>
    private sealed record ComplexCompositionLaneState(
        string Name,
        bool Modified,
        object?[]? Current,
        object?[]? Original,
        object?[]? OriginalValues
    );

    /// <summary>Observes EF's cached lanes independently of the production journal's access delegates.</summary>
    private static class ComplexCompositionNativeAccess
    {
        private static readonly Type s_nativeBase = typeof(IUpdateEntry).Assembly.GetType(
            "Microsoft.EntityFrameworkCore.ChangeTracking.Internal.InternalEntryBase",
            throwOnError: true)!;

        private static readonly Type s_nativeComplex = typeof(IUpdateEntry).Assembly.GetType(
            "Microsoft.EntityFrameworkCore.ChangeTracking.Internal.InternalComplexEntry",
            throwOnError: true)!;

        private static readonly Type s_infrastructure = typeof(MemberEntry)
            .GetInterfaces()
            .Single(type =>
                type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(IInfrastructure<>)
                && type
                    .GetGenericArguments()[0].Name
                == "IInternalEntry");

        private static readonly FieldInfo s_lanes = s_nativeBase.GetField(
            "_complexCollectionEntries",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly ConstructorInfo s_property = typeof(PropertyEntry).GetConstructor(
        [
            s_infrastructure
                .GetGenericArguments()[0],
            typeof(IProperty),
        ])!;

        private static readonly MethodInfo s_original = s_nativeBase
            .GetMethods()
            .Single(method =>
                method.Name == "GetOriginalValue"
                && !method.IsGenericMethod
                && method.GetParameters() is [{ ParameterType: var parameter }]
                && parameter == typeof(IPropertyBase));

        private static readonly MethodInfo s_modified = s_nativeBase.GetMethod(
            "IsModified",
            [typeof(IComplexProperty)])!;

        /// <summary>Reads the exact invariant infrastructure interface of EF's member wrapper.</summary>
        internal static object Instance(
            MemberEntry member
        )
            => s_infrastructure.GetProperty("Instance")!.GetValue(member)!;

        /// <summary>Wraps a retained native identity without resolving it through a mutable CLR ordinal.</summary>
        internal static PropertyEntry Property(
            object native,
            IProperty property
        )
            => (PropertyEntry)s_property.Invoke([native, property]);

        /// <summary>Reads actual native state without invoking public recursive state setters.</summary>
        internal static EntityState State(
            object native
        )
            => (EntityState)s_nativeBase.GetProperty("EntityState")!.GetValue(native)!;

        /// <summary>Reads physical element ordinals, distinguishing the entity owner from complex elements.</summary>
        internal static int? Ordinal(
            object native,
            bool original
        )
            => s_nativeComplex.IsInstanceOfType(native)
                ? (int)s_nativeComplex.GetProperty(
                    original
                        ? "OriginalOrdinal"
                        : "Ordinal")!.GetValue(native)!
                : null;

        /// <summary>Copies the cached lists without EF's capacity initializer or filtered enumeration.</summary>
        internal static ComplexCompositionLaneState Lane(
            object native,
            IComplexProperty property
        )
        {
            var cache = (Array)s_lanes.GetValue(native)!;
            var lane = cache.GetValue(property.GetIndex())!;
            var current = (IList?)lane
                .GetType()
                .GetField(
                    "_entries",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lane);

            var original = (IList?)lane
                .GetType()
                .GetField(
                    "_originalEntries",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lane);

            var values = (IEnumerable?)s_original.Invoke(native, [property]);

            return new ComplexCompositionLaneState(
                property.Name,
                (bool)s_modified.Invoke(native, [property])!,
                current
                    ?.Cast<object?>()
                    .ToArray(),
                original
                    ?.Cast<object?>()
                    .ToArray(),
                values
                    ?.Cast<object?>()
                    .ToArray());
        }
    }
}
