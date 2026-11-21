using Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;
using Doka.EntityFrameworkCore.NestedSet.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes wide detached plans with sentinel and fully prefilled rollback input values.</summary>
public class BulkPlanMeasurementBenchmarks : IDisposable
{
    private TrackingHierarchy.Context _context = null!;
    private NestedSetStore<TrackingHierarchy.Node, int, Guid, int> _store = null!;
    private NestedSetBranch<TrackingHierarchy.Node> _root = null!;

    /// <summary>Releases owned fixture resources independently of semantic verification.</summary>
    public void Dispose()
    {
        try
        {
            Dispose(disposing: true);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Releases managed fixture resources once, including failed or incomplete setup.</summary>
    /// <param name="disposing">Whether this call owns managed-resource cleanup.</param>
    protected virtual void Dispose(
        bool disposing
    )
    {
        if (!disposing)
        {
            return;
        }

        var context = _context;
        _context = null!;
        context?.Dispose();
    }

    /// <summary>Gets or sets the complete original wide-plan population.</summary>
    [Params(20_000)]
    public int Nodes { get; set; } = 20_000;

    /// <summary>Gets or sets whether every child starts with caller-owned non-sentinel structure.</summary>
    [Params(false, true)]
    public bool Prefilled { get; set; }

    /// <summary>Constructs detached topology and mapped store outside native measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _context = new TrackingHierarchy.Context();
        _store = new NestedSetStore<TrackingHierarchy.Node, int, Guid, int>(
            _context, _context.Model.FindEntityType(typeof(TrackingHierarchy.Node))!, 1, Guid.Empty);
        var children = Enumerable.Range(2, Nodes - 1)
            .Select(id => new NestedSetBranch<TrackingHierarchy.Node>(new TrackingHierarchy.Node
            {
                NodeId = id,
                Tree = Prefilled ? 7 : 0,
                TreeId = Prefilled ? new Guid("11111111-1111-1111-1111-111111111111") : Guid.Empty,
                Parent = Prefilled ? 999 : null,
                Start = Prefilled ? 17 : 0,
                End = Prefilled ? 18 : 0,
                Depth = Prefilled ? 9 : 0,
                Position = Prefilled ? 11 : 0,
            })).ToArray();
        _root = new NestedSetBranch<TrackingHierarchy.Node>(new TrackingHierarchy.Node { NodeId = 1 }, children);
        VerifyWorkload();
    }

    /// <summary>Creates one complete plan without installing geometry or tracking its detached inputs.</summary>
    /// <returns>The real production plan, retained by the native benchmark consumer.</returns>
    [Benchmark]
    public object BuildPlan() => CreatePlan();

    /// <summary>Requires complete topology and unchanged caller input outside measured invocations.</summary>
    public void VerifyWorkload()
    {
        var plan = CreatePlan();
        if (plan.Nodes.Count != Nodes || _context.ChangeTracker.Entries().Any()
            || _root.Children.Any(child => child.Entity.Start != (Prefilled ? 17 : 0)))
        {
            throw new InvalidOperationException("The wide bulk-plan workload changed detached input semantics.");
        }
    }

    /// <summary>Verifies completion and releases its disconnected provider services.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            VerifyWorkload();
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>Retains the actual constructor's input indexes and rollback capture in the observation.</summary>
    private NestedSetBulkPlan<TrackingHierarchy.Node, int, Guid, int> CreatePlan()
        => new(
            _store,
            [_root],
            new HashSet<TrackingHierarchy.Node>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);
}
