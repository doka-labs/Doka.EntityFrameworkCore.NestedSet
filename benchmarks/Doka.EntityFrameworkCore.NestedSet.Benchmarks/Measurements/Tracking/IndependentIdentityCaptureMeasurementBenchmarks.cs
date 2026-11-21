namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Observes independent root identity capture while keeping tracked rows and result slots outside measurement.
/// </summary>
public class IndependentIdentityCaptureMeasurementBenchmarks : IDisposable
{
    private InsertionIdentityMeasurementBenchmarks.AllocationContext _context = null!;
    private EntityEntry[] _entries = null!;
    private NestedSetInsertionTracking.Identity[] _identities = null!;

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

    /// <summary>Gets or sets the complete original population of independently captured roots.</summary>
    [Params(10_000)]
    public int Roots { get; set; } = 10_000;

    /// <summary>Creates independent real EF entries and aligned result slots before measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _context = new InsertionIdentityMeasurementBenchmarks.AllocationContext();
        _entries = new EntityEntry[Roots];
        _identities = new NestedSetInsertionTracking.Identity[Roots];
        // WHY: Distinct assigned keys and aligned slots are required by the independent-root workload.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Roots; index++)
        {
            _entries[index] = _context.Add(new InsertionIdentityMeasurementBenchmarks.IntNode { Id = index + 1 });
        }
    }

    /// <summary>Captures every distinct root once, retaining all results in their existing aligned slots.</summary>
    /// <returns>The complete independent identity wave.</returns>
    [Benchmark]
    public object CaptureRoots()
    {
        // WHY: Index alignment retains every distinct root's capture without a new per-invocation result buffer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _entries.Length; index++)
        {
            _identities[index] = NestedSetInsertionTracking.Capture(_entries[index]);
        }

        return _identities;
    }

    /// <summary>Requires each result to match its own input identity before releasing the fixture.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (_identities.Any(identity => identity is null))
            {
                throw new InvalidOperationException("The independent identity wave was not completely captured.");
            }

            // WHY: Validate each result against the exact entry at the same position.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _entries.Length; index++)
            {
                var property = _entries[index].Metadata.FindPrimaryKey()!.Properties[0];
                if (!_identities[index].Matches(property))
                {
                    throw new InvalidOperationException(
                        "An independently captured identity no longer matches its row.");
                }
            }
        }
        finally
        {
            Dispose();
        }
    }
}
