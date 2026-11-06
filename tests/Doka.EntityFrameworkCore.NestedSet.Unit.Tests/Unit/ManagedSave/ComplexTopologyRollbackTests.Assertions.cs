namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Observes actual CLR instances and native EF values before capturing the library snapshot.</summary>
    private static GraphObservation Observe(
        EntityEntry<TopologyPayload> entry
    )
    {
        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var rows = entry.Entity.Rows;
        var items = new ItemObservation[rows?.Count ?? 0];

        if (rows is not null)
        {
            // WHY: Match each caller element with its actual native ordinal before any callback changes topology.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < rows.Count; index++)
            {
                var item = rows[index];
                var element = collection[index];
                items[index] = new ItemObservation(
                    item,
                    element.State,
                    ObserveScalar(element.Property(nameof(TopologyItem.Text))),
                    ObserveDetails(element.ComplexProperty(nameof(TopologyItem.Details)), item.Details),
                    ObserveLeaves(element.ComplexCollection(nameof(TopologyItem.Leaves)), item.Leaves));
            }
        }

        return new GraphObservation(
            entry.State,
            ObserveScalar(entry.Property(value => value.Marker)),
            rows,
            collection.IsModified,
            items,
            ObserveDetails(entry.ComplexProperty(value => value.Optional), entry.Entity.Optional));
    }

    /// <summary>Retains the original nested list and each actual native element's independent property slots.</summary>
    private static LeavesObservation ObserveLeaves(
        ComplexCollectionEntry collection,
        List<TopologyLeaf>? leaves
    )
    {
        var values = new LeafObservation[leaves?.Count ?? 0];

        if (leaves is not null)
        {
            // WHY: Native element state belongs to the same ordinal as the independently retained CLR instance.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < leaves.Count; index++)
            {
                var element = collection[index];
                values[index] = new LeafObservation(
                    leaves[index],
                    element.State,
                    ObserveScalar(element.Property(nameof(TopologyLeaf.Text))));
            }
        }

        return new LeavesObservation(leaves, collection.IsModified, values);
    }

    /// <summary>Separates optional instance identity from immutable scalar and flag observations.</summary>
    private static DetailsObservation ObserveDetails(
        ComplexPropertyEntry entry,
        TopologyDetails? instance
    )
        => new(
            instance,
            instance?.Text,
            instance?.Code,
            entry.IsModified,
            ObserveScalar(entry.Property(nameof(TopologyDetails.Text))),
            ObserveScalar(entry.Property(nameof(TopologyDetails.Code))));

    /// <summary>Reads public native values without detecting or accepting any pending changes.</summary>
    private static ScalarObservation ObserveScalar(
        PropertyEntry property
    )
        => new(property.CurrentValue, property.OriginalValue, property.IsModified);

    /// <summary>Checks the complete preimage directly after rollback, without a repairing DetectChanges call.</summary>
    private static void AssertGraph(
        EntityEntry<TopologyPayload> entry,
        GraphObservation before
    )
    {
        Assert.Equal(before.State, entry.State);
        AssertScalar(entry.Property(value => value.Marker), before.Marker);
        Assert.Equal(before.Marker.Current, entry.Entity.Marker);
        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);

        Assert.Equal(7, entry.Entity.Id);
        Assert.Same(before.Rows, entry.Entity.Rows);
        AssertDetails(entry.ComplexProperty(value => value.Optional), entry.Entity.Optional, before.Optional);
        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));

        Assert.Equal(before.RowsModified, collection.IsModified);
        Assert.Equal(before.Items.Length, entry.Entity.Rows?.Count ?? 0);

        if (entry.Entity.Rows is not { } rows)
        {
            return;
        }

        // WHY: Content equality cannot detect a replaced list, cloned elements or native entries shifted by removal.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Items.Length; index++)
        {
            var expected = before.Items[index];
            var element = collection[index];

            Assert.Same(expected.Instance, rows[index]);
            Assert.Equal(expected.State, element.State);
            AssertScalar(element.Property(nameof(TopologyItem.Text)), expected.Text);
            Assert.Equal(expected.Text.Current, rows[index].Text);
            AssertDetails(element.ComplexProperty(nameof(TopologyItem.Details)), rows[index].Details, expected.Details);
            AssertLeaves(element.ComplexCollection(nameof(TopologyItem.Leaves)), rows[index].Leaves, expected.Leaves);
        }
    }

    /// <summary>Checks nested list identity, order, original values and native element state.</summary>
    private static void AssertLeaves(
        ComplexCollectionEntry collection,
        List<TopologyLeaf>? leaves,
        LeavesObservation before
    )
    {
        Assert.Same(before.Instance, leaves);
        Assert.Equal(before.Modified, collection.IsModified);
        Assert.Equal(before.Values.Length, leaves?.Count ?? 0);

        if (leaves is null)
        {
            return;
        }

        // WHY: Preserve ordinal pairing for caller-owned nested references and the corresponding native state.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Values.Length; index++)
        {
            var expected = before.Values[index];
            var element = collection[index];

            Assert.Same(expected.Instance, leaves[index]);
            Assert.Equal(expected.State, element.State);
            AssertScalar(element.Property(nameof(TopologyLeaf.Text)), expected.Text);
            Assert.Equal(expected.Text.Current, leaves[index].Text);
        }
    }

    /// <summary>Checks optional-reference identity separately from native original slots.</summary>
    private static void AssertDetails(
        ComplexPropertyEntry entry,
        TopologyDetails? instance,
        DetailsObservation before
    )
    {
        Assert.Same(before.Instance, instance);
        Assert.Same(instance, entry.CurrentValue);
        Assert.Equal(before.RawText, instance?.Text);
        Assert.Equal(before.RawCode, instance?.Code);
        Assert.Equal(before.Modified, entry.IsModified);
        AssertScalar(entry.Property(nameof(TopologyDetails.Text)), before.Text);
        AssertScalar(entry.Property(nameof(TopologyDetails.Code)), before.Code);
    }

    /// <summary>Rejects restoration that loses original values or modification flags.</summary>
    private static void AssertScalar(
        PropertyEntry property,
        ScalarObservation before
    )
    {
        Assert.Equal(before.Current, property.CurrentValue);
        Assert.Equal(before.Original, property.OriginalValue);
        Assert.Equal(before.Modified, property.IsModified);
    }

    /// <summary>Retains the entity's separately observed scalar, graph and native state.</summary>
    private sealed record GraphObservation(
        EntityState State,
        ScalarObservation Marker,
        List<TopologyItem>? Rows,
        bool RowsModified,
        ItemObservation[] Items,
        DetailsObservation Optional
    );

    /// <summary>Retains one exact complex element and its independent nested containers.</summary>
    private sealed record ItemObservation(
        TopologyItem Instance,
        EntityState State,
        ScalarObservation Text,
        DetailsObservation Details,
        LeavesObservation Leaves
    );

    /// <summary>Retains the caller's nested list independently of its mutable contents.</summary>
    private sealed record LeavesObservation(
        List<TopologyLeaf>? Instance,
        bool Modified,
        LeafObservation[] Values
    );

    /// <summary>Retains one exact nested CLR element and its native state and scalar slots.</summary>
    private sealed record LeafObservation(
        TopologyLeaf Instance,
        EntityState State,
        ScalarObservation Text
    );

    /// <summary>Retains raw optional-reference identity and independent immutable scalar observations.</summary>
    private sealed record DetailsObservation(
        TopologyDetails? Instance,
        string? RawText,
        string? RawCode,
        bool Modified,
        ScalarObservation Text,
        ScalarObservation Code
    );

    /// <summary>Retains distinct native current/original values and modification intent.</summary>
    private sealed record ScalarObservation(
        object? Current,
        object? Original,
        bool Modified
    );
}
