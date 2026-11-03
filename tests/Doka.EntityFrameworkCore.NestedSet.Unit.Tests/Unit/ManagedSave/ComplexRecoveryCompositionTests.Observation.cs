using System.Collections;
using TopologyDetails = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyDetails;
using TopologyItem = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyItem;
using TopologyLeaf = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyLeaf;
using TopologyPayload = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyPayload;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexRecoveryCompositionTests
{
    private static readonly PropertyInfo s_memberOwner = typeof(MemberEntry)
        .GetProperty("InternalEntry", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly ConstructorInfo s_scalarConstructor = typeof(PropertyEntry)
        .GetConstructors()
        .Single(constructor => constructor.GetParameters() is var parameters
            && parameters.Length == 2
            && parameters[1].ParameterType == typeof(IProperty));

    /// <summary>Captures native identities and scalar state independently of production verification.</summary>
    private static Preimage Observe(
        EntityEntry<TopologyPayload> entry
    )
    {
        var graph = ObserveClr(entry.Entity);
        var root = s_memberOwner.GetValue(entry.Property(value => value.Id))!;
        var native = new List<NativeObservation>();
        ReadNative(root,
            entry.Metadata,
            "payload",
            native,
            new HashSet<object>(ReferenceEqualityComparer.Instance),
            hasCurrentPath: true);

        return new Preimage(graph, native.ToArray());
    }

    /// <summary>Retains mutable caller containers and values without installing or detecting native entries.</summary>
    private static ClrObservation ObserveClr(
        TopologyPayload payload
    )
        => new(payload.Marker,
            payload.Rows,
            payload.Rows
                ?.Select(row => new ClrItemObservation(
                    row,
                    row.Text,
                    Details(row.Details),
                    row.Leaves,
                    row.Leaves
                        ?.Select(leaf => new ClrLeafObservation(leaf, leaf.Text))
                        .ToArray()
                    ?? []))
                .ToArray()
            ?? [],
            Details(payload.Optional));

    /// <summary>Copies immutable scalar values while retaining exact optional-complex identity.</summary>
    private static ClrDetailsObservation Details(
        TopologyDetails? value
    )
        => new(value, value?.Text, value?.Code);

    /// <summary>Reads exact EF backing slots, including empty lengths and null holes.</summary>
    /// <remarks>
    ///     Reflection is read-only and belongs solely to this independent test oracle. It does not call the
    ///     production native bridge, create complex element wrappers or repair collection membership.
    /// </remarks>
    private static void ReadNative(
        object owner,
        ITypeBase metadata,
        string path,
        List<NativeObservation> result,
        HashSet<object> visited,
        bool hasCurrentPath
    )
    {
        if (!visited.Add(owner))
        {
            return;
        }

        var ordinalProperty = owner
            .GetType()
            .GetProperty("Ordinal");

        var ordinal = (int?)ordinalProperty?.GetValue(owner);
        var originalOrdinal = (int?)owner
            .GetType()
            .GetProperty("OriginalOrdinal")
            ?.GetValue(owner);

        var state = (EntityState)owner
            .GetType()
            .GetProperty("EntityState")!.GetValue(owner)!;

        var hasCurrent = hasCurrentPath && (ordinal is null or >= 0);
        var scalars = metadata
            .GetFlattenedProperties()
            .Select(property =>
            {
                var scalar = (PropertyEntry)s_scalarConstructor.Invoke([owner, property]);
                // WHY: A deleted original or any child below it has no current CLR path. Original values and flags
                // remain observable even when a child's own ordinal is valid below an absent ancestor.

                return new NativeScalarObservation(property,
                    hasCurrent,
                    hasCurrent ? scalar.CurrentValue : null,
                    scalar.OriginalValue,
                    scalar.IsModified,
                    scalar.IsTemporary);
            })
            .ToArray();

        var cache = (Array)FindField(owner.GetType(), "_complexCollectionEntries")
            .GetValue(owner)!;

        var collections = new List<NativeCollectionObservation>();

        foreach (var collection in metadata
                     .GetFlattenedComplexProperties()
                     .Where(property => property.IsCollection))
        {
            var lane = cache.GetValue(collection.GetIndex())!;
            var current = ReadLane(lane, "_entries");
            var original = ReadLane(lane, "_originalEntries");
            collections.Add(new NativeCollectionObservation(collection, current, original));

            foreach (var child in current
                         .Concat(original)
                         .Where(child => child is not null))
            {
                ReadNative(child!, collection.ComplexType, $"{path}.{collection.Name}", result, visited, hasCurrent);
            }
        }

        result.Add(new NativeObservation(owner,
            metadata,
            path,
            state,
            ordinal,
            originalOrdinal,
            scalars,
            collections.ToArray()));
    }

    /// <summary>Reads exact lane length and null holes without EF capacity getters or element construction.</summary>
    private static object?[] ReadLane(
        object lane,
        string name
    )
        => FindField(lane.GetType(), name)
            .GetValue(lane) is IList entries
            ? entries
                .Cast<object?>()
                .ToArray()
            : [];

    /// <summary>Finds a declared framework backing field across native entry subtypes.</summary>
    private static FieldInfo FindField(
        Type type,
        string name
    )
    {
        // WHY: Traverse every declaring type because DeclaredOnly cannot find its private base fields.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                is { } field)
            {
                return field;
            }
        }

        throw new InvalidOperationException($"The independent EF observation field '{name}' was not found.");
    }

    /// <summary>Compares both authorities before any detecting retry can normalize a broken native preimage.</summary>
    private static void AssertPreimage(
        EntityEntry<TopologyPayload> entry,
        Preimage before
    )
    {
        AssertClr(entry.Entity, before.Clr);
        var root = s_memberOwner.GetValue(entry.Property(value => value.Id))!;
        var actual = new List<NativeObservation>();
        ReadNative(root,
            entry.Metadata,
            "payload",
            actual,
            new HashSet<object>(ReferenceEqualityComparer.Instance),
            hasCurrentPath: true);
        AssertNative(before.Native, actual, exactLanes: true);
    }

    /// <summary>Proves capture preserves caller semantics and tracking intent across lazy cache growth.</summary>
    private static void AssertCaptureState(
        Preimage before,
        Preimage captured
    )
    {
        AssertClr(captured.Clr, before.Clr);
        AssertNative(before.Native, captured.Native, exactLanes: false);
    }

    /// <summary>Compares independent observations with optional exact callback-boundary capacities.</summary>
    private static void AssertNative(
        NativeObservation[] before,
        IReadOnlyList<NativeObservation> actual,
        bool exactLanes
    )
    {
        Assert.Equal(before.Length, actual.Count);

        // WHY: Exact native identity and ordinal pairing distinguishes successful recovery from recreating entries.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Length; index++)
        {
            var expected = before[index];
            var restored = Assert.Single(actual, candidate => ReferenceEquals(candidate.Owner, expected.Owner));
            Assert.Same(expected.Metadata, restored.Metadata);
            Assert.Equal(expected.Path, restored.Path);
            Assert.Equal(expected.State, restored.State);
            Assert.Equal(expected.Ordinal, restored.Ordinal);
            Assert.Equal(expected.OriginalOrdinal, restored.OriginalOrdinal);
            Assert.Equal(expected.Scalars, restored.Scalars);
            Assert.Equal(expected.Collections.Length, restored.Collections.Length);

            // WHY: Comparing only non-null entries misses a trailing hole that later breaks EF change detection.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var collectionIndex = 0; collectionIndex < expected.Collections.Length; collectionIndex++)
            {
                var expectedCollection = expected.Collections[collectionIndex];
                var restoredCollection = restored.Collections[collectionIndex];
                Assert.Same(expectedCollection.Metadata, restoredCollection.Metadata);

                if (!exactLanes)
                {
                    AssertExistingSlots(expectedCollection.Current, restoredCollection.Current);
                    AssertExistingSlots(expectedCollection.Original, restoredCollection.Original);

                    continue;
                }

                AssertLane(expectedCollection.Current,
                    restoredCollection.Current,
                    $"{expected.Path}.{expectedCollection.Metadata.Name}: current");
                AssertLane(expectedCollection.Original,
                    restoredCollection.Original,
                    $"{expected.Path}.{expectedCollection.Metadata.Name}: original");
            }
        }
    }

    /// <summary>Permits only trailing null slots during capture, retaining all native entry identities.</summary>
    private static void AssertExistingSlots(
        object?[] before,
        object?[] captured
    )
    {
        Assert.True(captured.Length >= before.Length, "Capture shortened an existing native lane.");

        // WHY: EF access may append lazy null slots, but the preexisting slots must retain exact identities.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Length; index++)
        {
            Assert.Same(before[index], captured[index]);
        }

        Assert.All(captured.Skip(before.Length), Assert.Null);
    }

    /// <summary>Requires the same native slot length, null-hole positions and exact entry identities.</summary>
    private static void AssertLane(
        object?[] expected,
        object?[] actual,
        string path
    )
    {
        Assert.True(expected.Length == actual.Length,
            $"{path}: expected lane length {expected.Length}; actual {actual.Length}.");

        // WHY: Reference equality includes null holes; value or filtered sequence equality would miss lane corruption.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.True(ReferenceEquals(expected[index], actual[index]), $"{path}: native slot {index} changed.");
        }
    }

    /// <summary>Checks raw CLR references and immutable scalars independently of native entry wrappers.</summary>
    private static void AssertClr(
        TopologyPayload payload,
        ClrObservation expected
    )
        => AssertClr(ObserveClr(payload), expected);

    /// <summary>Checks immutable CLR observations without rereading an earlier boundary's mutable containers.</summary>
    private static void AssertClr(
        ClrObservation actual,
        ClrObservation expected
    )
    {
        Assert.Equal(expected.Marker, actual.Marker);
        Assert.Same(expected.Rows, actual.Rows);
        Assert.Equal(expected.Optional, actual.Optional);
        Assert.Equal(expected.Items.Length, actual.Items.Length);

        // WHY: Every ordinal retains caller-owned element, nested list and leaf identities after composed changes.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < expected.Items.Length; index++)
        {
            var row = actual.Items[index];
            var item = expected.Items[index];
            Assert.Same(item.Instance, row.Instance);
            Assert.Equal(item.Text, row.Text);
            Assert.Equal(item.Details, row.Details);
            Assert.Same(item.Leaves, row.Leaves);
            Assert.Equal(item.Values.Length, row.Values.Length);

            // WHY: Leaf scalar copies must be compared against their original objects, not mutated retained lists.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var leafIndex = 0; leafIndex < item.Values.Length; leafIndex++)
            {
                Assert.Same(item.Values[leafIndex].Instance, row.Values[leafIndex].Instance);
                Assert.Equal(item.Values[leafIndex].Text, row.Values[leafIndex].Text);
            }
        }
    }

    /// <summary>Retains independently observed CLR and native state without invoking production verification.</summary>
    private sealed record Preimage(
        ClrObservation Clr,
        NativeObservation[] Native
    );

    /// <summary>Retains top-level caller-owned containers and independent scalar copies.</summary>
    private sealed record ClrObservation(
        string Marker,
        List<TopologyItem>? Rows,
        ClrItemObservation[] Items,
        ClrDetailsObservation Optional
    );

    /// <summary>Retains one caller element and its optional and nested scalar preimages.</summary>
    private sealed record ClrItemObservation(
        TopologyItem Instance,
        string Text,
        ClrDetailsObservation Details,
        List<TopologyLeaf>? Leaves,
        ClrLeafObservation[] Values
    );

    /// <summary>Retains an exact caller leaf and its immutable scalar preimage.</summary>
    private sealed record ClrLeafObservation(
        TopologyLeaf Instance,
        string Text
    );

    /// <summary>Retains nullable optional-complex identity and immutable scalar values.</summary>
    private sealed record ClrDetailsObservation(
        TopologyDetails? Instance,
        string? Text,
        string? Code
    );

    /// <summary>Retains native identity, lifecycle state, scalar flags and both collection lanes.</summary>
    private sealed record NativeObservation(
        object Owner,
        ITypeBase Metadata,
        string Path,
        EntityState State,
        int? Ordinal,
        int? OriginalOrdinal,
        NativeScalarObservation[] Scalars,
        NativeCollectionObservation[] Collections
    );

    /// <summary>Retains public current/original scalar values and independent native scalar flags.</summary>
    private sealed record NativeScalarObservation(
        IProperty Metadata,
        bool HasCurrent,
        object? Current,
        object? Original,
        bool Modified,
        bool Temporary
    );

    /// <summary>Retains exact current and original slot membership, including lengths and null holes.</summary>
    private sealed record NativeCollectionObservation(
        IComplexProperty Metadata,
        object?[] Current,
        object?[] Original
    );
}
