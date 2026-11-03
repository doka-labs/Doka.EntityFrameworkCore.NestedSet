using System.Globalization;
using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackAllocationTests
{
    /// <summary>Bounds the native journal independently of the scalar snapshot and its EF property wrappers.</summary>
    /// <param name="rows">The number of complex rows, each containing two nested reference leaves.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(2_048)]
    [InlineData(20_000)]
    public async Task AlignedNativeCaptureAvoidsTransientIdentityMaps(
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
        var expectedScalars = ObserveScalars(entry);
        var expectedStates = ObserveStates(entry);
        var owner = NestedSetInsertionContract.Instance.Entry(entry);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();

        // Act
        var snapshot = new NestedSetComplexSnapshot(entry, owner);
        var completed = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        // WHY: Isolating journal construction catches a reintroduced per-collection identity map without
        // tightening the existing complete scalar-capture or recovery budgets against unrelated EF costs.
        var maximum = 262_144L + (rows * 800L);
        var elapsed = Stopwatch.GetElapsedTime(started, completed);
        _output.WriteLine($"Aligned native capture: rows={rows}; allocated={allocated} B; maximum={maximum} B; "
            + $"elapsed={elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms.");

        snapshot.Verify();
        Assert.Equal(expectedScalars, ObserveScalars(entry));
        Assert.Equal(expectedStates, ObserveStates(entry));
        Assert.Same(owner, NestedSetInsertionContract.Instance.Entry(entry));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
        Assert.InRange(allocated, 0, maximum);
    }
}
