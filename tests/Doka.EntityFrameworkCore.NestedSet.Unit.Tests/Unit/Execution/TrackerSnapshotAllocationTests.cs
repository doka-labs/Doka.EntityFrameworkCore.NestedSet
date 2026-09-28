namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Measures snapshot allocation independently of database I/O and verifies mutable rollback values.</summary>
public sealed partial class TrackerSnapshotAllocationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains measured bytes in the test result rather than relying on a manually copied benchmark.</summary>
    public TrackerSnapshotAllocationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Bounds warm snapshot allocation for a representative large tracked unit of work.</summary>
    [Fact]
    public async Task TwentyThousandEntitiesUseCompactScalarSnapshots()
    {
        // Arrange
        await using var context = new SnapshotContext();
        context.AttachRange(Enumerable.Range(1, 20_000).Select(id => new SnapshotScalars { Id = id }));
        _ = NestedSetTrackerSnapshot.Capture(context);
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        _output.WriteLine($"Tracked entities: 20000; scalar properties: 15; allocated bytes: {allocated}");
        // WHY: The 60 MB budget leaves about 24% headroom over the measured 48.48 MB compact snapshot.
        Assert.InRange(allocated, 1, 60_000_000);
        GC.KeepAlive(snapshot);
        Assert.Equal(20_000, context.ChangeTracker.Entries().Count());
    }

    /// <summary>Preserves an unchanged mutable value's pre-callback contents through snapshot restoration.</summary>
    [Fact]
    public async Task UnchangedMutablePayloadRetainsItsOwnedRollbackSnapshot()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entity = new SnapshotPayload { Id = 1, Values = [1, 2, 3] };
        context.Attach(entity);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        entity.Values.Add(4);
        context.ChangeTracker.DetectChanges();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal<int>([1, 2, 3], entity.Values);
        Assert.Equal(EntityState.Unchanged, context.Entry(entity).State);
        Assert.Equal<int>([1, 2, 3], context.Entry(entity).Property(value => value.Values).OriginalValue);
        Assert.False(context.Entry(entity).Property(value => value.Values).IsModified);
    }
}
