using System.Collections;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Copies direct caller identities and values without relying on live collection aliases.</summary>
    private static ComplexCallbackClrState CaptureComplexCallbackClr(
        ComplexCallbackRow row
    )
        => new(
            row,
            row.Items,
            row
                .Items
                ?.Select(item => new ComplexCallbackItemState(
                    item,
                    item.Text,
                    item.Parts,
                    item
                        .Parts
                        ?.Select(part => new ComplexCallbackLeafState(part, part.Text))
                        .ToArray()
                    ?? []))
                .ToArray()
            ?? [],
            row.Address,
            row.Address?.Text,
            row.Address?.Tags,
            row
                .Address
                ?.Tags
                ?.Select(tag => new ComplexCallbackLeafState(tag, tag.Text))
                .ToArray()
            ?? []);

    /// <summary>Captures public EF complex entries separately from caller-owned containers.</summary>
    private static ComplexCallbackNativeState[] CaptureComplexCallbackNative(
        ComplexCallbackGraph graph
    )
    {
        var states = new List<ComplexCallbackNativeState>();
        CaptureComplexCallbackCollection(graph.Row.ComplexCollection(value => value.Items), "Items", "Parts", states);
        var address = graph.Row.ComplexProperty(value => value.Address);
        states.Add(
            new ComplexCallbackNativeState(
                "Address",
                address.CurrentValue,
                null,
                address.IsModified,
                CaptureComplexPeerLeaves(address.Properties),
                []));

        if (address.CurrentValue is not null)
        {
            CaptureComplexCallbackCollection(address.ComplexCollection("Tags"), "Address.Tags", null, states);
        }

        return states.ToArray();
    }

    /// <summary>Records fixture ordinals and originals without reading original-slot current values.</summary>
    private static void CaptureComplexCallbackCollection(
        ComplexCollectionEntry collection,
        string path,
        string? nestedName,
        List<ComplexCallbackNativeState> states
    )
    {
        var originals = Enumerable
            .Range(0, 2)
            .SelectMany(index => collection
                .GetOriginalEntry(index)
                .Properties
                .OrderBy(property => property.Metadata.Name, StringComparer.Ordinal)
                .Select(property => new ComplexPeerOriginalLeafState(
                    index.ToString(CultureInfo.InvariantCulture) + ":" + property.Metadata.Name,
                    property.OriginalValue)))
            .ToArray();

        states.Add(
            new ComplexCallbackNativeState(
                path,
                collection.CurrentValue,
                null,
                collection.IsModified,
                [],
                originals));

        if (collection.CurrentValue is not IList values)
        {
            return;
        }

        // WHY: Public EF complex entries are indexed by the caller's current ordinal, not by entity identity.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < values.Count; index++)
        {
            var entry = collection[index];
            var elementPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            states.Add(
                new ComplexCallbackNativeState(
                    elementPath,
                    values[index],
                    entry.State,
                    entry.IsModified,
                    CaptureComplexPeerLeaves(entry.Properties),
                    []));

            if (nestedName is not null)
            {
                CaptureComplexCallbackCollection(
                    entry.ComplexCollection(nestedName),
                    elementPath + "." + nestedName,
                    null,
                    states);
            }
        }
    }

    /// <summary>Reads real stored payload independently of the tested tracker and its nullable CLR graph.</summary>
    private static async Task<ComplexCallbackStoredRow> ReadComplexCallbackRowAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await using var verification = await CreateComplexCallbackContextAsync(
            database,
            cancellationToken: cancellationToken);

        var row = await verification
            .Set<ComplexCallbackRow>()
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        return new ComplexCallbackStoredRow(
            row.Label,
            row.Version,
            row.Address?.Text,
            row.Items is null
                ? null
                : string.Join(
                    "|",
                    row.Items.Select(item => item.Text
                        + ":"
                        + (item.Parts is null
                            ? "null"
                            : string.Join(",", item.Parts.Select(part => part.Text))))),
            row.Address?.Tags is null
                ? null
                : string.Join("|", row.Address.Tags.Select(tag => tag.Text)));
    }
}
