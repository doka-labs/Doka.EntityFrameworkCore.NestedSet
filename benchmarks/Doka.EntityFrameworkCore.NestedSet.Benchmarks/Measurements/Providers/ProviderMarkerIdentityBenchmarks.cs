using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes the actual Doka source-binding and ordered-join marker identities.</summary>
public class ProviderMarkerIdentityBenchmarks : IDisposable
{
    private ProviderMeasurementContext _owner = null!;
    private NestedSetProviderCapabilities _adapter = null!;

    /// <summary>Gets or sets the configured engine using Doka's real adapter.</summary>
    [Params("MySql", "MariaDb")]
    public string Engine { get; set; } = "MySql";

    /// <summary>Initializes the exact selected adapter outside measured property reads.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _owner = new ProviderMeasurementContext(Engine);
        _adapter = NestedSetProviderCapabilities.Resolve(_owner.Context);
        Validate();
    }

    /// <summary>Reads the initialized source-binding marker.</summary>
    /// <returns>The exact configured public provider method.</returns>
    [Benchmark]
    public MethodInfo? CompletedSource() => _adapter.CompletedSourceMethod;

    /// <summary>Reads the initialized ordered-join marker.</summary>
    /// <returns>The exact configured public provider method.</returns>
    [Benchmark]
    public MethodInfo? OrderedJoin() => _adapter.OrderedJoinMethod;

    /// <summary>Requires non-null stable marker identities independently of allocation values.</summary>
    public void Validate()
    {
        var source = CompletedSource();
        var join = OrderedJoin();
        if (source is null || join is null
            || !ReferenceEquals(source, CompletedSource()) || !ReferenceEquals(join, OrderedJoin()))
        {
            throw new InvalidOperationException("A provider marker identity changed.");
        }
    }

    /// <summary>Validates and releases the setup-owned provider services.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            Validate();
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>Releases owned setup state independently of semantic cleanup validation.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed setup resources when disposal was explicitly requested.</summary>
    /// <param name="disposing">Whether managed state can be released.</param>
    protected virtual void Dispose(
        bool disposing
    )
    {
        if (disposing)
        {
            _owner?.Dispose();
        }
    }
}
