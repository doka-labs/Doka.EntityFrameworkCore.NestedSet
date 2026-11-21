using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Doka.EntityFrameworkCore.NestedSet.Tests;
using SharingContext = Doka.EntityFrameworkCore.NestedSet.Tests.ManagedSharingCaptureTests.SharingContext;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures real same-row closure independently from unrelated tracked rows and callback-list reset.</summary>
public class SharingCaptureBenchmarks
{
    private ManagedSaveContextOwner<SharingContext>? _owner;
    private IUpdateEntry _seed = null!;
    private readonly List<IUpdateEntry> _callbacks = new(4);
    private int _count;

    /// <summary>Gets or sets the complete unrelated tracker population.</summary>
    [Params(1, 128, 2_048)]
    public int Rows { get; set; }

    /// <summary>Gets or sets whether the selected write has four actual same-row peers or one regular FK row.</summary>
    [Params(false, true)]
    public bool Sharing { get; set; }

    /// <summary>Creates the exact shared-table, owned, inherited and deliberately non-shared fixture model.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _owner = ManagedSaveContextOwner<SharingContext>.Create(static options => new SharingContext(options), false);
        var roots = Enumerable
            .Range(1, Rows)
            .Select(id => ManagedSharingCaptureTests.CreateRoot(id, id % 2 == 0))
            .ToArray();
        _owner.Context.AttachRange(roots);
        var selected = Sharing
            ? (object)roots[0]
            : roots[0].Related[0];

        var entry = _owner.Context.Entry(selected);
        entry.Property(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))
            .CurrentValue = "Pending selected payload";
        _seed = NestedSetInsertionContract.Instance.Entry(entry);
    }

    /// <summary>Resets only the bounded callback input outside the measured discovery operation.</summary>
    [IterationSetup]
    public void PrepareIteration()
    {
        _callbacks.Clear();
        _callbacks.Add(_seed);
        _count = 0;
    }

    /// <summary>Executes one real closure discovery through the current context.</summary>
    /// <returns>The exact number of entries sharing the selected physical row.</returns>
    [Benchmark]
    public int Capture()
    {
        NestedSetSaveChanges.IncludeManagedSharingPeers(_owner!.Context, _callbacks);
        _count = _callbacks.Count;

        return _count;
    }

    /// <summary>Rejects missing peers, accidental whole-tracker capture and state or connection mutation.</summary>
    [IterationCleanup]
    public void ValidateIteration()
    {
        if (_count != (Sharing ? 4 : 1)
            || _callbacks.Distinct(ReferenceEqualityComparer.Instance).Count() != _count
            || _seed.EntityState != EntityState.Modified
            || !_seed.IsModified(_seed.EntityType.FindProperty(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))!)
            || _owner!.Context.ChangeTracker.AutoDetectChangesEnabled
            || _owner.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
        {
            throw new InvalidOperationException("Managed sharing capture changed its selected physical-row closure.");
        }
    }

    /// <summary>Releases the actual context before its provider graph.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _owner?.Dispose();
        _owner = null;
    }
}
