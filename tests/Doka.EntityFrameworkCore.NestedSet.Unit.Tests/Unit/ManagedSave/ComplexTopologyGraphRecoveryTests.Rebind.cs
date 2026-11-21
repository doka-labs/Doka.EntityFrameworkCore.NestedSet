using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyGraphRecoveryTests
{
    /// <summary>Complete detected-removal recovery preserves native state with its original native state.</summary>
    /// <param name="rows">The number of captured complex elements, each retaining two nested reference leaves.</param>
    [Theory]
    [InlineData(128)]
    [InlineData(2_048)]
    public async Task Restore_DetectedCallbackClear_RebindsOriginalNativeEntries(
        int rows
    )
    {
        // Arrange
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
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        // WHY: Native Deleted entries and invalidated current ordinals require the full rebind path. Callback
        // explicit consumer detection invalidates native ordinal lanes before the asserted recovery.
        originalRows.Clear();
        payload.Optional = null;
        context.ChangeTracker.DetectChanges();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(snapshotEntry, NestedSetInsertionContract.Instance.Entry(entry));
        Assert.Equal(originalState, entry.State);
        Assert.Same(originalRows, payload.Rows);
        Assert.Same(optional, payload.Optional);
        Assert.Equal(rows, originalRows.Count);

        // WHY: Match each restored ordinal to its original caller object; equal values cannot detect cloned graphs.
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
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

}
