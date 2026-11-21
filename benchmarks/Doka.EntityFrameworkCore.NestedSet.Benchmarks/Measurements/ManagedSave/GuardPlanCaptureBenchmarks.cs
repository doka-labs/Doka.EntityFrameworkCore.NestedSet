using Doka.EntityFrameworkCore.NestedSet.Tests;
using SnapshotContext = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.SnapshotContext;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures the actual private candidate-plan capture through the existing shared fixture seam.</summary>
public class GuardPlanCaptureBenchmarks
{
    private ManagedSaveContextOwner<SnapshotContext>? _owner;
    private List<EntityEntry> _entries = null!;
    private object? _result;

    /// <summary>Gets or sets a complete former candidate-capture workload.</summary>
    [ParamsSource(nameof(Cases))]
    public GuardPlanBenchmarkCase Case { get; set; }

    /// <summary>Gets all original empty, sparse and selected-multi-candidate profiles.</summary>
    public static IEnumerable<GuardPlanBenchmarkCase> Cases =>
    [
        new(0, 0), new(1024, 0), new(1, 1), new(1024, 1), new(128, 64),
    ];

    /// <summary>Creates the exact tracked/candidate sets outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _owner = ManagedSaveContextOwner<SnapshotContext>.Create(static options => new SnapshotContext(options), true);
        var nodes = Enumerable.Range(1, Case.Nodes).Select(HierarchyChangeDetectionTests.CreateNode).ToArray();
        _owner.Context.AttachRange(nodes);
        _entries = nodes.Take(Case.Candidates).Select(node => (EntityEntry)_owner.Context.Entry(node)).ToList();

        foreach (var entry in _entries)
        {
            entry.Property(nameof(HierarchyChangeDetectionTests.ProbeNode.Name)).IsModified = true;
        }

        // WHY: Bind the existing private API seam before BenchmarkDotNet measures actual plan creation.
        _ = HierarchyChangeDetectionTests.PlanCount(HierarchyChangeDetectionTests.CapturePlan([]));
    }

    /// <summary>Captures exactly the actual authorized candidate list.</summary>
    /// <returns>The real retained guard plan.</returns>
    [Benchmark]
    public object Capture() => _result = HierarchyChangeDetectionTests.CapturePlan(_entries);

    /// <summary>Checks candidate authorization and independent tracker/connection state.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (_result is null
                || HierarchyChangeDetectionTests.PlanCount(_result) != Case.Candidates
                || HierarchyChangeDetectionTests.PlanHasChanges(_owner!.Context, _result)
                || _owner.Context.ChangeTracker.Entries().Count() != Case.Nodes
                || _owner.Context.ChangeTracker.AutoDetectChangesEnabled
                || _owner.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
            {
                throw new InvalidOperationException("Guard-plan capture changed its exact authorization contract.");
            }
        }
        finally
        {
            _owner?.Dispose();
            _owner = null;
        }
    }
}
