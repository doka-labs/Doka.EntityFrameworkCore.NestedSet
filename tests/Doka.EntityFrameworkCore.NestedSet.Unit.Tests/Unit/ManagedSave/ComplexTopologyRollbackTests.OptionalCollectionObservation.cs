namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Observes optional reference identity separately from current and original native lanes.</summary>
    private static DocumentObservation ObserveDocument(
        EntityEntry<OptionalCollectionPayload> entry,
        int originalCount
    )
    {
        var reference = entry.ComplexProperty(value => value.Document);
        var collection = reference.ComplexCollection(nameof(OptionalDocument.Children));
        var instance = entry.Entity.Document;
        var children = instance?.Children;
        var current = children is null
            ? []
            : children
                .Select((child, index) => new DocumentChildObservation(
                    child,
                    collection[index].State,
                    ObserveScalar(
                        collection[index]
                            .Property(nameof(OptionalChild.Text)))))
                .ToArray();

        // WHY: Cached native entries may remain installed when a raw-null optional container hides their CLR path.
        // State and original buffers are observable without invoking their unavailable current CLR scalar getter.
        var installed = Enumerable
            .Range(0, originalCount)
            .Select(index => collection[index].State)
            .ToArray();

        var originals = Enumerable
            .Range(0, originalCount)
            .Select(index => ObserveOriginal(collection.GetOriginalEntry(index)))
            .ToArray();

        return new DocumentObservation(
            entry.State,
            ObserveScalar(entry.Property(value => value.Marker)),
            instance,
            ObserveScalar(reference.Property(nameof(OptionalDocument.Title))),
            reference.IsModified,
            children,
            collection.IsModified,
            current,
            installed,
            originals);
    }

    /// <summary>Verifies literal null, exact containers and native lanes before any further change detection.</summary>
    private static void AssertDocument(
        EntityEntry<OptionalCollectionPayload> entry,
        DocumentObservation before
    )
    {
        Assert.Equal(before.State, entry.State);
        AssertScalar(entry.Property(value => value.Marker), before.Marker);
        Assert.Same(before.Instance, entry.Entity.Document);
        var reference = entry.ComplexProperty(value => value.Document);
        var collection = reference.ComplexCollection(nameof(OptionalDocument.Children));

        Assert.Equal(before.ReferenceModified, reference.IsModified);
        AssertScalar(reference.Property(nameof(OptionalDocument.Title)), before.Title);
        Assert.Same(before.Children, entry.Entity.Document?.Children);
        Assert.Equal(before.Current.Length, entry.Entity.Document?.Children?.Count ?? 0);
        Assert.Equal(before.CollectionModified, collection.IsModified);

        // WHY: Current CLR members and installed native slots are distinct when the captured parent is raw-null.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Current.Length; index++)
        {
            var expected = before.Current[index];
            var element = collection[index];
            var actual = entry.Entity.Document!.Children![index];

            Assert.Same(expected.Instance, actual);
            Assert.Equal(expected.State, element.State);
            AssertScalar(element.Property(nameof(OptionalChild.Text)), expected.Text);
            Assert.Equal(expected.Text.Current, actual.Text);
        }

        // WHY: Original-only observations avoid pretending that a hidden CLR element has a readable current value.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Originals.Length; index++)
        {
            Assert.Equal(before.InstalledStates[index], collection[index].State);
            AssertOriginal(collection.GetOriginalEntry(index), before.Originals[index]);
        }

        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);
    }

    /// <summary>Reads an existing original buffer and flag without requiring a current CLR path.</summary>
    private static OriginalChildObservation ObserveOriginal(
        ComplexElementEntry element
    )
    {
        var property = element.Property("Text");

        return new OriginalChildObservation(element.State, property.OriginalValue, property.IsModified);
    }

    /// <summary>Checks original native identity roles after callback deletion or accepted slot retirement.</summary>
    private static void AssertOriginal(
        ComplexElementEntry element,
        OriginalChildObservation before
    )
    {
        var property = element.Property("Text");

        Assert.Equal(before.State, element.State);
        Assert.Equal(before.Value, property.OriginalValue);
        Assert.Equal(before.Modified, property.IsModified);
    }

    /// <summary>Retains callback native handles while their CLR container remains reachable.</summary>
    private static ComplexElementEntry[] ReadDocumentEntries(
        EntityEntry<OptionalCollectionPayload> entry,
        int count
    )
    {
        var collection = entry
            .ComplexProperty(value => value.Document)
            .ComplexCollection(nameof(OptionalDocument.Children));

        return Enumerable
            .Range(0, count)
            .Select(index => collection[index])
            .ToArray();
    }

    /// <summary>Creates stored non-null reference and collection topology without database initialization.</summary>
    private static OptionalCollectionPayload CreateOptionalPayload()
        => new()
        {
            Id = 7,
            Document = new OptionalDocument
            {
                Title = "Stored document",
                Children =
                [
                    new OptionalChild { Text = "Stored child first" },
                    new OptionalChild { Text = "Stored child last" },
                ],
            },
        };

    /// <summary>Creates a genuinely new callback container and native child candidates.</summary>
    private static OptionalDocument CreateCallbackDocument()
        => new()
        {
            Title = "Callback document",
            Children =
            [
                new OptionalChild { Text = "Callback child first" },
                new OptionalChild { Text = "Callback child last" },
            ],
        };
}
