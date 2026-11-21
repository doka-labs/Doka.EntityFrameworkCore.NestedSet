namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes stable guard traversal at empty, singleton and candidate-block boundaries.</summary>
public class TrackedIdentityMeasurementBenchmarks : IDisposable
{
    private TrackingHierarchy.Context _context = null!;
    private NestedSetTrackedIdentityGuard<TrackingHierarchy.Node, int, Guid, int> _guard = null!;

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
        var guard = _guard;
        _guard = null!;
        try
        {
            guard?.Dispose();
        }
        finally
        {
            context?.Dispose();
        }
    }

    /// <summary>Gets or sets the original candidate-block population.</summary>
    [Params(0, 1, 63, 64, 65)]
    public int Candidates { get; set; }

    /// <summary>Captures the real guard independently of measured verification.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _context = new TrackingHierarchy.Context();
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        var metadata = _context.Model.FindEntityType(typeof(TrackingHierarchy.Node))!;
        var mapping = NestedSetMapping<TrackingHierarchy.Node, int, int>.For(_context, metadata);
        _context.AttachRange(Enumerable.Range(1, Candidates).Select(id => new TrackingHierarchy.Node
        {
            NodeId = id,
            Tree = 7,
            TreeId = Guid.NewGuid(),
            Start = 1,
            End = 2,
        }));
        _guard = NestedSetTrackedIdentityGuard<TrackingHierarchy.Node, int, Guid, int>.Capture(
            _context, mapping,
            [new NestedSetTreeLockRequest<Guid, int>(metadata, 7, Guid.NewGuid(), NestedSetTreeLockMode.Existing)])!;
        VerifyWorkload();
    }

    /// <summary>Verifies the existing captured candidates through the production guard.</summary>
    [Benchmark]
    public void VerifyUnchanged() => _guard.VerifyUnchanged();

    /// <summary>Requires unchanged candidate membership without an allocation threshold.</summary>
    public void VerifyWorkload()
    {
        VerifyUnchanged();
        if (_guard.HasCandidates != (Candidates != 0)
            || _context.ChangeTracker.Entries<TrackingHierarchy.Node>().Count() != Candidates
            || _context.ChangeTracker.AutoDetectChangesEnabled)
        {
            throw new InvalidOperationException("The captured identity-guard workload changed.");
        }
    }

    /// <summary>Releases the guard before disposing the context it observes.</summary>
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
}
