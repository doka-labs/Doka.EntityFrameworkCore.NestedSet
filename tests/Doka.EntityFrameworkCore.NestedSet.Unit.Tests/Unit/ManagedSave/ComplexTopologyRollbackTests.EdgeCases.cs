using System.Collections;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Raw null slots and duplicate references retain their exact pending ordinal topology.</summary>
    /// <param name="nullTail">Whether the pending tail is null instead of a second reference to the first item.</param>
    /// <param name="detectCallback">Whether EF detects the callback's empty or null graph.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RawNullOrDuplicatePreimageSurvivesCallback(
        bool nullTail,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackGraph(context);
        var rows = entry.Entity.Rows!;

        if (nullTail)
        {
            // WHY: IList can express a real caller-supplied null despite this model's non-null element annotation.
            // No native entry is fabricated for that absent tail slot, and no pre-capture detection is requested.
            ((IList)rows).Add(null);
        }
        else
        {
            rows.Add(rows[0]);
        }

        var before = ObserveEdgeGraph(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        if (nullTail)
        {
            entry.Entity.Rows = null;
        }
        else
        {
            rows.Clear();
        }

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Unchanged, before.State);
        Assert.False(before.Modified);
        Assert.Equal(3, before.Items.Length);
        AssertEdgeGraph(entry, before);

        if (nullTail)
        {
            Assert.Null(entry.Entity.Rows![2]);
        }
        else
        {
            Assert.Same(entry.Entity.Rows![0], entry.Entity.Rows[2]);
            Assert.Equal(EntityState.Detached, before.Items[2]!.State);
        }

        AssertSettings(context);
    }

    /// <summary>Already Added or Deleted entities retain their distinct native lane semantics.</summary>
    /// <param name="deleted">Whether the captured entity is Deleted instead of Added.</param>
    /// <param name="accepted">Whether EF accepted the callback graph before a later transaction failure.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ContainingLifecyclePreservesNativeLanes(
        bool deleted,
        bool accepted
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var payload = new TopologyPayload
        {
            Id = 7,
            Rows =
            [
                new TopologyItem
                {
                    Text = "Stored lifecycle first",
                    Leaves = [new TopologyLeaf()],
                },
                new TopologyItem
                {
                    Text = "Stored lifecycle last",
                    Leaves = [new TopologyLeaf()],
                },
            ],
        };

        var entry = deleted
            ? context.Attach(payload)
            : await context.AddAsync(payload, CancellationToken.None);

        _ = context.Model.GetRelationalModel();

        if (deleted)
        {
            context.Remove(payload);
        }

        payload.Marker = "Pending lifecycle marker";
        var before = ObserveEdgeGraph(entry);
        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var originals = deleted
            ? Enumerable
                .Range(0, 2)
                .Select(index =>
                {
                    var original = collection.GetOriginalEntry(index);

                    return new OriginalElementObservation(original.State,
                        ObserveScalar(original.Property(nameof(TopologyItem.Text))));
                })
                .ToArray()
            : [];

        var snapshot = NestedSetTrackerSnapshot.Capture(context, [Update(context, payload)]);
        payload.Rows!.Clear();
        payload.Optional = null;
        context.ChangeTracker.DetectChanges();

        if (accepted)
        {
            context.ChangeTracker.AcceptAllChanges();
        }

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(deleted ? EntityState.Deleted : EntityState.Added, before.State);
        AssertEdgeGraph(entry, before);

        // WHY: Added entries have no authoritative original lane. Deleted entries instead retain original-only
        // native entries whose -1 current ordinals must never be reassigned as ordinary live positions.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < originals.Length; index++)
        {
            var expected = originals[index];
            var original = collection.GetOriginalEntry(index);

            Assert.Equal(EntityState.Deleted, expected.State);
            Assert.Equal(expected.State, original.State);
            AssertScalar(original.Property(nameof(TopologyItem.Text)), expected.Text);
        }

        AssertSettings(context);
    }

    /// <summary>Observes nullable and duplicate CLR slots without indexing a native entry for a null element.</summary>
    private static EdgeGraphObservation ObserveEdgeGraph(
        EntityEntry<TopologyPayload> entry
    )
    {
        var rows = entry.Entity.Rows
            ?? throw new InvalidOperationException("The edge-case preimage must contain its caller-owned list.");

        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var originalLane = entry.State == EntityState.Deleted;
        var items = rows
            .Select((item, index) =>
            {
                if (item is null)
                {
                    return null;
                }

                // WHY: A Deleted containing entry has original-only native elements. This fixture retains its
                // unchanged CLR list separately; public current-lane indexing is forbidden while its owner is Deleted.
                var element = originalLane ? collection.GetOriginalEntry(index) : collection[index];

                return new ItemObservation(item,
                    element.State,
                    ObserveScalar(element.Property(nameof(TopologyItem.Text))),
                    ObserveDetails(element.ComplexProperty(nameof(TopologyItem.Details)), item.Details),
                    ObserveEdgeLeaves(element.ComplexCollection(nameof(TopologyItem.Leaves)),
                        item.Leaves,
                        originalLane));
            })
            .ToArray();

        return new EdgeGraphObservation(entry.State,
            ObserveScalar(entry.Property(value => value.Marker)),
            rows,
            collection.IsModified,
            items,
            ObserveDetails(entry.ComplexProperty(value => value.Optional), entry.Entity.Optional));
    }

    /// <summary>Checks raw slots and native entries without normalizing the captured preimage.</summary>
    private static void AssertEdgeGraph(
        EntityEntry<TopologyPayload> entry,
        EdgeGraphObservation before
    )
    {
        Assert.Equal(before.State, entry.State);
        AssertScalar(entry.Property(value => value.Marker), before.Marker);
        Assert.Equal(before.Marker.Current, entry.Entity.Marker);
        Assert.Same(before.Rows, entry.Entity.Rows);
        var rows = Assert.IsType<List<TopologyItem>>(entry.Entity.Rows);
        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var originalLane = before.State == EntityState.Deleted;

        Assert.Equal(before.Items.Length, rows.Count);
        Assert.Equal(before.Modified, collection.IsModified);
        AssertDetails(entry.ComplexProperty(value => value.Optional), entry.Entity.Optional, before.Optional);

        // WHY: Null slots have no element entry, while duplicate CLR references still occupy distinct ordinals.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Items.Length; index++)
        {
            var expected = before.Items[index];

            if (expected is null)
            {
                Assert.Null(rows[index]);
                continue;
            }

            var element = originalLane ? collection.GetOriginalEntry(index) : collection[index];

            Assert.Same(expected.Instance, rows[index]);
            Assert.Equal(expected.State, element.State);
            AssertScalar(element.Property(nameof(TopologyItem.Text)), expected.Text);
            Assert.Equal(expected.Text.Current, rows[index].Text);
            AssertDetails(element.ComplexProperty(nameof(TopologyItem.Details)),
                rows[index].Details,
                expected.Details);
            AssertEdgeLeaves(element.ComplexCollection(nameof(TopologyItem.Leaves)),
                rows[index].Leaves,
                expected.Leaves,
                originalLane);
        }

        Assert.False(entry.Property(value => value.Id).IsModified);
    }

    /// <summary>Observes original-only nested entries without invoking the Deleted parent's current lane.</summary>
    private static LeavesObservation ObserveEdgeLeaves(
        ComplexCollectionEntry collection,
        List<TopologyLeaf>? leaves,
        bool originalLane
    )
    {
        if (!originalLane)
        {
            return ObserveLeaves(collection, leaves);
        }

        var values = new LeafObservation[leaves?.Count ?? 0];

        if (leaves is not null)
        {
            // WHY: The unchanged CLR topology and its original native lane share ordinals in this Deleted fixture.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < leaves.Count; index++)
            {
                var original = collection.GetOriginalEntry(index);
                values[index] = new LeafObservation(leaves[index],
                    original.State,
                    ObserveScalar(original.Property(nameof(TopologyLeaf.Text))));
            }
        }

        return new LeavesObservation(leaves, collection.IsModified, values);
    }

    /// <summary>Checks original-only native state and independently retained CLR list and member identity.</summary>
    private static void AssertEdgeLeaves(
        ComplexCollectionEntry collection,
        List<TopologyLeaf>? leaves,
        LeavesObservation before,
        bool originalLane
    )
    {
        if (!originalLane)
        {
            AssertLeaves(collection, leaves, before);

            return;
        }

        Assert.Same(before.Instance, leaves);
        Assert.Equal(before.Modified, collection.IsModified);
        Assert.Equal(before.Values.Length, leaves?.Count ?? 0);

        if (leaves is null)
        {
            return;
        }

        // WHY: Deleted native elements are available only by original ordinal, independently of CLR membership.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Values.Length; index++)
        {
            var expected = before.Values[index];
            var original = collection.GetOriginalEntry(index);

            Assert.Same(expected.Instance, leaves[index]);
            Assert.Equal(expected.State, original.State);
            AssertScalar(original.Property(nameof(TopologyLeaf.Text)), expected.Text);
            Assert.Equal(expected.Text.Current, leaves[index].Text);
        }
    }

    /// <summary>Retains raw list topology while representing a null slot without an invented native wrapper.</summary>
    private sealed record EdgeGraphObservation(
        EntityState State,
        ScalarObservation Marker,
        List<TopologyItem> Rows,
        bool Modified,
        ItemObservation?[] Items,
        DetailsObservation Optional
    );
}
