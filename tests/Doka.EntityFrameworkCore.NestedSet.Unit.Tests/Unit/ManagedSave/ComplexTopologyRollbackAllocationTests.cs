using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;
using TopologyDetails = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyDetails;
using TopologyItem = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyItem;
using TopologyLeaf = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyLeaf;
using TopologyPayload = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyPayload;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Measures complete complex-graph capture and exceptional restoration independently of setup.</summary>
[Trait("Category", "Allocation")]
[Collection("Allocation measurements")]
public sealed partial class ComplexTopologyRollbackAllocationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains separate capture and restoration observations in the test output.</summary>
    public ComplexTopologyRollbackAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>A single wide complex graph restores its original topology within linear allocation budgets.</summary>
    /// <param name="rows">The complex-element count within one tracked entity, with two leaves per element.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(2_048)]
    [InlineData(20_000)]
    public async Task FullCaptureAndExceptionalRestoreHaveLinearGraphAllocation(
        int rows
    )
    {
        // Arrange
        await WarmAsync();
        await using var context = new TopologyContext();
        var payload = CreatePayload(rows);
        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        MarkPending(entry);
        var originalRows = payload.Rows!;
        var items = originalRows.ToArray();
        var details = items
            .Select(item => item.Details)
            .ToArray();

        var leafLists = items
            .Select(item => item.Leaves!)
            .ToArray();

        var leaves = leafLists
            .Select(list => list.ToArray())
            .ToArray();

        var optional = payload.Optional;
        var expectedScalars = ObserveScalars(entry);
        var expectedStates = ObserveStates(entry);
        var originalState = entry.State;
        var snapshotEntry = NestedSetInsertionContract.Instance.Entry(entry);
        var beforeCapture = GC.GetAllocatedBytesForCurrentThread();

        // Act
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var captureAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeCapture;
        // WHY: The failure changes accepted originals, references and list membership. Keep that callback work
        // outside both measurements; the second interval charges the complete exceptional Restore operation.
        var failure = Record.Exception((Action)(() =>
        {
            context.ChangeTracker.AcceptAllChanges();
            payload.Optional!.Text = "Callback optional";
            payload.Optional = null;

            foreach (var item in items)
            {
                item.Text = "Callback item";
                item.Details = null;
                item.Leaves![0].Text = "Callback leaf";
                item.Leaves.Clear();
            }

            originalRows.Clear();
            payload.Rows = [];

            throw new TopologyCallbackFailure();
        }));

        var beforeRestore = GC.GetAllocatedBytesForCurrentThread();
        snapshot.Restore();
        var restoreAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeRestore;

        // Assert
        // WHY: Fixed metadata/runtime headroom is separate from a per-element allowance. One root with a
        // growing list exposes repeated full-list copies that tests of many tiny entities would conceal.
        var captureMaximum = 262_144L + rows * 8_192L;
        var restoreMaximum = 262_144L + rows * 16_384L;
        _output.WriteLine($"Complex topology: entities=1; rows={rows}; nested leaves={rows * 2}; "
            + $"capture={captureAllocated} B/{captureMaximum} B; restore={restoreAllocated} B/{restoreMaximum} B.");
        Assert.IsType<TopologyCallbackFailure>(failure);
        Assert.InRange(captureAllocated, 0, captureMaximum);
        Assert.InRange(restoreAllocated, 0, restoreMaximum);
        Assert.Same(snapshotEntry, NestedSetInsertionContract.Instance.Entry(entry));
        Assert.Equal(originalState, entry.State);
        Assert.Same(originalRows, payload.Rows);
        Assert.Same(optional, payload.Optional);
        Assert.Equal(rows, originalRows.Count);

        // WHY: Preserve exact original membership by ordinal; scalar equality alone accepts replacement clones.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < rows; index++)
        {
            Assert.Same(items[index], originalRows[index]);
            Assert.Same(details[index], items[index].Details);
            Assert.Same(leafLists[index], items[index].Leaves);
            Assert.Equal(2, leafLists[index].Count);
            Assert.Same(leaves[index][0], leafLists[index][0]);
            Assert.Same(leaves[index][1], leafLists[index][1]);
        }

        Assert.Equal(expectedScalars, ObserveScalars(entry));
        Assert.Equal(expectedStates, ObserveStates(entry));
        Assert.Single(context.ChangeTracker.Entries());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Builds independent complex instances and nested list storage for one physical entity.</summary>
    private static TopologyPayload CreatePayload(
        int rows
    )
        => new()
        {
            Id = 7,
            Rows = Enumerable
                .Range(0, rows)
                .Select(index => new TopologyItem
                {
                    Text = $"Stored item {index}",
                    Details = new TopologyDetails
                    {
                        Text = $"Stored details {index}",
                        Code = $"Code {index}",
                    },
                    Leaves =
                    [
                        new TopologyLeaf { Text = $"Stored first leaf {index}" },
                        new TopologyLeaf { Text = $"Stored second leaf {index}" },
                    ],
                })
                .ToList(),
        };

    /// <summary>Separates pending flags and raw unmarked complex values from their stored originals.</summary>
    private static void MarkPending(
        EntityEntry<TopologyPayload> entry
    )
    {
        var payload = entry.Entity;
        payload.Marker = "Pending marker";
        entry.Property(value => value.Marker).IsModified = true;
        payload.Rows![0].Text = "Pending first item";
        entry.ComplexCollection(nameof(TopologyPayload.Rows))[0]
            .Property(nameof(TopologyItem.Text)).IsModified = true;
        payload.Optional!.Text = "Raw unmarked optional";
        payload.Rows[^1].Details!.Code = "Raw unmarked final code";
    }

    /// <summary>Warms shared metadata and property restoration against a different, unchanged topology.</summary>
    private static async Task WarmAsync()
    {
        await using var context = new TopologyContext();
        var entry = context.Attach(CreatePayload(1));
        _ = context.Model.GetRelationalModel();
        MarkPending(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        snapshot.Restore();
    }

    /// <summary>Records scalar current/original values and flags before callbacks mutate any container.</summary>
    private static ScalarObservation[] ObserveScalars(
        EntityEntry<TopologyPayload> entry
    )
    {
        var result = new List<ScalarObservation>();

        void Add(
            PropertyEntry property
        )
            => result.Add(new ScalarObservation(property.CurrentValue, property.OriginalValue, property.IsModified));

        foreach (var property in entry.Properties)
        {
            Add(property);
        }

        foreach (var property in entry.ComplexProperty(value => value.Optional).Properties)
        {
            Add(property);
        }

        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var rows = entry.Entity.Rows!;

        // WHY: Native complex entries correspond to stable original ordinals, including each nested leaf slot.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < rows.Count; index++)
        {
            var element = collection[index];
            Add(element.Property(nameof(TopologyItem.Text)));

            foreach (var property in element.ComplexProperty(nameof(TopologyItem.Details)).Properties)
            {
                Add(property);
            }

            var nested = element.ComplexCollection(nameof(TopologyItem.Leaves));

            // WHY: Pair the native leaf entry with the corresponding immutable expected scalar observation.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var leaf = 0; leaf < rows[index].Leaves!.Count; leaf++)
            {
                Add(nested[leaf].Property(nameof(TopologyLeaf.Text)));
            }
        }

        return result.ToArray();
    }

    /// <summary>Records independent aggregate flags and native element states without a mutable oracle.</summary>
    private static ComplexObservation[] ObserveStates(
        EntityEntry<TopologyPayload> entry
    )
    {
        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var result = new List<ComplexObservation>
        {
            new(entry.State,
                entry.ComplexProperty(value => value.Optional).IsModified),
            new(null, collection.IsModified),
        };

        var rows = entry.Entity.Rows!;

        // WHY: A shrinking callback can shift native indices; retain the state expected at every original slot.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < rows.Count; index++)
        {
            var element = collection[index];
            result.Add(new ComplexObservation(element.State, element.IsModified));
            result.Add(new ComplexObservation(null,
                element.ComplexProperty(nameof(TopologyItem.Details)).IsModified));
            var nested = element.ComplexCollection(nameof(TopologyItem.Leaves));
            result.Add(new ComplexObservation(null, nested.IsModified));

            // WHY: Element state is independent of each property's flag and must survive nested topology loss.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var leaf = 0; leaf < rows[index].Leaves!.Count; leaf++)
            {
                result.Add(new ComplexObservation(nested[leaf].State, nested[leaf].IsModified));
            }
        }

        return result.ToArray();
    }

    /// <summary>Keeps immutable expected scalar values separate from the mutable complex CLR graph.</summary>
    private readonly record struct ScalarObservation(
        object? Current,
        object? Original,
        bool Modified
    );

    /// <summary>Retains native states and flags without keeping a post-callback graph as the expected value.</summary>
    private readonly record struct ComplexObservation(
        EntityState? State,
        bool Modified
    );

    /// <summary>Distinguishes the intended callback failure from capture or restoration errors.</summary>
    private sealed class TopologyCallbackFailure : Exception;
}
