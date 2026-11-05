using System.Globalization;
using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackAllocationTests
{
    /// <summary>Complete detected-removal recovery preserves native state within the existing graph budget.</summary>
    /// <param name="rows">The number of captured complex elements, each retaining two nested reference leaves.</param>
    [Theory]
    [InlineData(128)]
    [InlineData(2_048)]
    public async Task DetectedCallbackClearRebindsNativeEntriesWithinGraphAllocation(
        int rows
    )
    {
        // Arrange
        await WarmAsync(cancellationToken: TestContext.Current.CancellationToken);
        await WarmDetectedComplexRebindAsync(cancellationToken: TestContext.Current.CancellationToken);
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
        // mutation and explicit consumer detection stay outside the measured Restore interval.
        originalRows.Clear();
        payload.Optional = null;
        context.ChangeTracker.DetectChanges();
        var beforeRestore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();

        // Act
        snapshot.Restore();
        var completed = Stopwatch.GetTimestamp();
        var restoreAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeRestore;

        // Assert
        var maximum = 262_144L + rows * 16_384L;
        var elapsed = Stopwatch.GetElapsedTime(started, completed);
        _output.WriteLine(
            $"Complex topology native rebind: entities=1; width={rows}; nested leaves={rows * 2}; "
            + $"restore={restoreAllocated} B/{maximum} B; "
            + $"elapsed={elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms.");

        Assert.InRange(restoreAllocated, 0, maximum);
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

    /// <summary>Warms the actual detected-removal rebind against a separate two-element graph.</summary>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task WarmDetectedComplexRebindAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var context = new TopologyContext();
        var payload = CreatePayload(2);
        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        MarkPending(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Rows!.Clear();
        payload.Optional = null;
        context.ChangeTracker.DetectChanges();
        snapshot.Restore();
    }
}
