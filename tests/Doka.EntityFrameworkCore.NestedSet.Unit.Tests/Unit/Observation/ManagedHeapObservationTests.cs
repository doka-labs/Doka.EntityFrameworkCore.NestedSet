using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks heap observations against independently counted, strongly held managed allocations.</summary>
[Collection("BulkPlanAllocation")]
public sealed class ManagedHeapObservationTests
{
    /// <summary>A preceding global count proves the newer anchor is before the recorded background cycle.</summary>
    /// <param name="snapshotIndex">The represented collection's start identity.</param>
    /// <param name="collections">The stable observed global collection count.</param>
    /// <param name="anchorCollections">The count paired with the newer allocation anchor.</param>
    [Theory]
    [InlineData(45, 45, 44)]
    [InlineData(48, 49, 44)]
    [InlineData(1, 1, 0)]
    public void BackgroundSnapshotUsesTheLatestProvenEarlierAnchor(
        long snapshotIndex,
        int collections,
        int anchorCollections
    )
    {
        // Arrange
        const long allocated = 800;
        const long recordedOccupied = 500;
        const long requiredOccupied = 900;

        // Act
        var anchor = ManagedHeapObservation.BackgroundIntervalStart(
            snapshotIndex,
            collections,
            (300, anchorCollections),
            100,
            knownEpoch: true);

        var observed = recordedOccupied + allocated - anchor;

        // Assert
        Assert.Equal(300, anchor);
        Assert.Equal(1000, observed);
        Assert.True(observed >= requiredOccupied);
    }

    /// <summary>Uncertain identities cannot discard allocations covered by the preceding full cycle.</summary>
    /// <param name="snapshotIndex">The represented collection's start identity.</param>
    /// <param name="collections">The accepted current global count.</param>
    /// <param name="anchorCollections">The count paired with the newer anchor.</param>
    /// <param name="knownEpoch">Whether the count history belongs to a known monotone epoch.</param>
    [Theory]
    [InlineData(45, 46, 46, true)]
    [InlineData(45, 46, 45, true)]
    [InlineData(46, 45, 44, true)]
    [InlineData(45, 45, -1, true)]
    [InlineData(45, -1, 44, true)]
    [InlineData(0, 0, 0, true)]
    [InlineData(45, 45, 44, false)]
    public void UncertainBackgroundIdentityKeepsTheEarlierAnchor(
        long snapshotIndex,
        int collections,
        int anchorCollections,
        bool knownEpoch
    )
    {
        // Arrange
        const long previousStart = 100;

        // Act
        var anchor = ManagedHeapObservation.BackgroundIntervalStart(
            snapshotIndex,
            collections,
            (300, anchorCollections),
            previousStart,
            knownEpoch);

        // Assert
        Assert.Equal(previousStart, anchor);
    }

    /// <summary>Completion followed by another full start must not advance past the recorded heap.</summary>
    [Fact]
    public void CompletionAndNextStartDoNotUnderstateOccupiedBytes()
    {
        // Arrange
        const long allocated = 8000;
        const long recordedOccupied = 5000;
        const long requiredOccupied = 7000;

        // Act
        var anchor = ManagedHeapObservation.BackgroundIntervalStart(2, 3, (7000, 2), 1000, knownEpoch: true);

        var observed = recordedOccupied + allocated - anchor;

        // Assert
        Assert.Equal(1000, anchor);
        Assert.True(observed >= requiredOccupied);
        Assert.True(recordedOccupied + allocated - 7000 < requiredOccupied);
    }

    /// <summary>The captured MySQL tuple does not charge the preceding cycle's 208 MB allocation traffic.</summary>
    [Fact]
    public void QualifiedBackgroundTupleExcludesPreviousCycleTraffic()
    {
        // Arrange
        // WHY: These are scalar values from the retained native million-node diagnostic, not an assumed
        // live-memory peak. The strict count identity proves the newer start precedes the recorded heap.
        const long allocated = 612_190_416;
        const long recordedOccupied = 127_382_824;
        const long baseline = 6_533_024;

        // Act
        var anchor = ManagedHeapObservation.BackgroundIntervalStart(
            45,
            45,
            (496_472_032, 44),
            288_506_688,
            knownEpoch: true);

        var observedAdditional = recordedOccupied + allocated - anchor - baseline;

        // Assert
        Assert.Equal(236_568_184, observedAdditional);
        Assert.True(observedAdditional >= recordedOccupied - baseline);
    }

    /// <summary>Samples retain pre-GC survivors and allocations made after the latest completed collection.</summary>
    /// <param name="collectBetween">Whether a completed collection separates the two held allocation waves.</param>
    /// <param name="releaseBaseline">Whether known baseline state becomes unreachable before that collection.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OccupiedObservationIncludesSurvivorsAndNewAllocations(
        bool collectBetween,
        bool releaseBaseline
    )
    {
        // Arrange
        var held = new byte[20][];
        var baselineState = CreateBaselineState(releaseBaseline);
        var observation = new ManagedHeapObservation();
        observation.Start();
        var completed = GC.GetGCMemoryInfo();
        var uncollected = 0L;

        // Act
        var firstAllocated = AllocateWave(0);
        var firstRequired = completed.HeapSizeBytes - completed.FragmentedBytes + uncollected;
        var firstIndex = completed.Index;
        var first = observation.Sample();
        var firstStable = GC.GetGCMemoryInfo().Index == firstIndex;

        if (collectBetween)
        {
            baselineState.Release();
            // WHY: Explicit collection exercises the sampler's GC-index transition; operation sampling itself
            // never forces collection or changes the measured code's garbage-collection behavior.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            completed = GC.GetGCMemoryInfo();
            uncollected = 0;
        }

        var secondAllocated = AllocateWave(held.Length / 2);
        var secondRequired = completed.HeapSizeBytes - completed.FragmentedBytes + uncollected;
        var secondIndex = completed.Index;
        var second = observation.Sample();
        var secondStable = GC.GetGCMemoryInfo().Index == secondIndex;

        // Assert
        Assert.True(first >= firstAllocated);
        Assert.True(second >= firstAllocated + secondAllocated);
        Assert.True(firstStable || secondStable, "At least one independent GC checkpoint must remain stable.");

        if (firstStable)
        {
            Assert.True(first >= firstRequired, $"Observed occupied bytes={first}; required={firstRequired}");
        }

        if (secondStable)
        {
            Assert.True(second >= secondRequired, $"Observed occupied bytes={second}; required={secondRequired}");
        }

        Assert.Equal(20, held.Count(value => value.Length == 100_000));

        if (baselineState.Released is { } released)
        {
            Assert.False(released.TryGetTarget(out _));
            // WHY: Reclaimed baseline state reduces net growth without removing either strongly held wave.
            Assert.True(second - observation.Baseline < firstAllocated + secondAllocated);
        }

        GC.KeepAlive(baselineState);
        GC.KeepAlive(held);
        return;

        long AllocateWave(
            int firstIndex
        )
        {
            var allocated = 0L;

            for (var index = firstIndex; index < firstIndex + (held.Length / 2); index++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                held[index] = new byte[100_000];
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                allocated += bytes;
                var latest = GC.GetGCMemoryInfo();

                if (latest.Index > completed.Index)
                {
                    // WHY: A newly completed collection can already include this allocation in its snapshot.
                    completed = latest;
                    uncollected = 0;
                }
                else
                {
                    uncollected += bytes;
                }
            }

            return allocated;
        }
    }

    /// <summary>Creates baseline state whose release is independent of the test method's JIT local lifetimes.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Action Release, WeakReference<byte[]>? Released) CreateBaselineState(
        bool releaseBaseline
    )
    {
        var noise = releaseBaseline ? new byte[4_000_000] : null;
        var released = noise is null ? null : new WeakReference<byte[]>(noise);

        return (() => noise = null, released);
    }

    /// <summary>
    /// Collected heap agrees with allocated object sizes instead of double-counting promoted survivors.
    /// </summary>
    [Fact]
    public void CollectedObservationMatchesHeldObjectAllocation()
    {
        // Arrange
        // WHY: The runner can release completed-test state between the two collections. A 2.4 MiB held
        // allocation keeps that fixed noise below the tolerance while still rejecting double-counted survivors.
        var held = new object[100_000];
        var before = ManagedHeapObservation.CollectedBytes();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var index = 0; index < held.Length; index++)
        {
            held[index] = new object();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var retained = ManagedHeapObservation.CollectedBytes() - before;

        // Assert
        // WHY: The ten-percent allowance covers fixed runner and observer state; it still rejects the
        // reproduced twofold survivor overcount rather than changing any production allocation budget.
        Assert.InRange(retained, allocated - (allocated / 10), allocated + (allocated / 10));
        Assert.All(held, Assert.NotNull);
        GC.KeepAlive(held);
    }
}
