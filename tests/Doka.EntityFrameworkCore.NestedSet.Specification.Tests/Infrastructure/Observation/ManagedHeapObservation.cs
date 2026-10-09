namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes retained heap and allocation-aware occupied bounds without platform-specific estimates.</summary>
internal sealed class ManagedHeapObservation
{
    private readonly Lock _gate = new();
    private long _gcIndex;
    private long _collectedOccupied;
    private long _intervalStart;
    private long _previousSampleAllocated;
    private int _fullCollections;
    private long _fullStart;
    private long _previousFullStart;
    private int _previousSampleCollections;
    private int _fullStartCollections;
    private bool _knownCollectionEpoch;

    /// <summary>Gets occupied bytes after the collected, caller-owned input baseline.</summary>
    internal long Baseline { get; private set; }

    /// <summary>Collects unreachable setup state before measurement, without collecting during the operation.</summary>
    internal void Start()
    {
        // WHY: This anchor must precede the collected heap snapshot. Capturing it afterward could exclude
        // allocations made between the snapshot and the first operation sample, including observer overhead.
        _intervalStart = _previousSampleAllocated = GC.GetTotalAllocatedBytes(precise: true);
        _fullStart = _previousFullStart = _intervalStart;
        var info = CollectInformation();
        Baseline = info.HeapSizeBytes - info.FragmentedBytes;
        _collectedOccupied = Baseline;
        _gcIndex = info.Index;
        _fullCollections = GC.CollectionCount(GC.MaxGeneration);

        // WHY: Labeling the pre-baseline allocation anchor with a later count is conservative: it may reject
        // an otherwise usable anchor, but cannot falsely prove that an allocation occurred before a collection.
        _fullStartCollections = _previousSampleCollections = GC.CollectionCount(0);
        _knownCollectionEpoch = _previousSampleCollections >= 0 && _gcIndex <= _previousSampleCollections;
    }

    /// <summary>Returns a conservative occupied-heap estimate including allocations since the last completed GC.</summary>
    /// <returns>Occupied bytes at this observation; not a peak between observations.</returns>
    internal long Sample()
    {
        lock (_gate)
        {
            GCMemoryInfo before;
            GCMemoryInfo after;
            long allocated;
            int fullBefore;
            int fullAfter;
            int collectionsBefore;
            int collectionsAfter;

            do
            {
                collectionsBefore = GC.CollectionCount(0);
                fullBefore = GC.CollectionCount(GC.MaxGeneration);
                before = GC.GetGCMemoryInfo();
                allocated = GC.GetTotalAllocatedBytes(precise: true);
                after = GC.GetGCMemoryInfo();
                fullAfter = GC.CollectionCount(GC.MaxGeneration);
                collectionsAfter = GC.CollectionCount(0);
            } while (before.Index != after.Index
                     || fullBefore != fullAfter
                     || collectionsBefore != collectionsAfter);

            _knownCollectionEpoch &= collectionsAfter >= 0 && collectionsAfter >= _previousSampleCollections;

            if (fullAfter != _fullCollections)
            {
                _previousFullStart = _fullStart;
                _fullStart = _previousSampleAllocated;
                _fullStartCollections = _previousSampleCollections;
                _fullCollections = fullAfter;
            }

            if (after.Index > _gcIndex)
            {
                // WHY: HeapSizeBytes describes the last completed GC, not allocations afterward. The previous
                // sample predates this GC; counting allocations from there can overcount but cannot omit newer ones.
                // WHY: A background GC snapshots heap before publishing completion. Full-GC counts advance at
                // start, so a paired global count can prove the current anchor predates this snapshot's cycle.
                // If another cycle started before the completed record was observed, retain the earlier anchor.
                // Blocking collections need only the prior sample; snapshot adoption stays monotone by index.
                _intervalStart = after.Concurrent
                    ? BackgroundIntervalStart(
                        after.Index,
                        collectionsAfter,
                        (_fullStart, _fullStartCollections),
                        _previousFullStart,
                        _knownCollectionEpoch)
                    : _previousSampleAllocated;

                _gcIndex = after.Index;
                _collectedOccupied = after.HeapSizeBytes - after.FragmentedBytes;
            }

            // WHY: Any can briefly return an older background record during completion publication. Retaining
            // the newer adopted snapshot and its allocation anchor avoids regressing to a stale, smaller heap.
            var occupied = _collectedOccupied + allocated - _intervalStart;
            _previousSampleAllocated = allocated;
            _previousSampleCollections = collectionsAfter;

            return occupied;
        }
    }

    /// <summary>Selects an allocation anchor that precedes the represented background collection.</summary>
    /// <param name="snapshotIndex">The collection-start identity of the completed snapshot.</param>
    /// <param name="collections">The stable current generation-zero collection count.</param>
    /// <param name="start">The current full-start anchor and its preceding sample's collection count.</param>
    /// <param name="previousStart">The preceding full-start allocation anchor.</param>
    /// <param name="knownEpoch">Whether accepted collection counts have remained nonnegative and monotone.</param>
    /// <returns>A conservative allocation anchor; uncertain identities keep the earlier start.</returns>
    internal static long BackgroundIntervalStart(
        long snapshotIndex,
        int collections,
        (long Allocated, int Collections) start,
        long previousStart,
        bool knownEpoch
    )
    {
        // WHY: .NET assigns snapshot indices at collection start. A stable preceding generation-zero count
        // strictly below that identity proves the newer allocation anchor is earlier than heap recording.
        // Completion/start races and unknown counter epochs cannot provide that proof and keep the older bound.
        if (knownEpoch
            && start.Collections >= 0
            && start.Collections < snapshotIndex
            && snapshotIndex <= collections)
        {
            return start.Allocated;
        }

        return Math.Min(previousStart, start.Allocated);
    }

    /// <summary>Returns actual retained bytes after a blocking collection for isolated before/after plan tests.</summary>
    internal static long CollectedBytes()
    {
        var info = CollectInformation();

        // WHY: GetTotalMemory double-counts small-object survivors on this qualified segmented .NET 10 runtime.
        // Completed-GC heap minus fragmentation also agrees with independently measured object allocation sizes.
        return info.HeapSizeBytes - info.FragmentedBytes;
    }

    /// <summary>Completes finalization and collection only at an explicit measurement boundary.</summary>
    private static GCMemoryInfo CollectInformation()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

        return GC.GetGCMemoryInfo();
    }
}
