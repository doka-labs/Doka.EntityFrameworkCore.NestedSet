using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes initialized selected-adapter resolution on every actual provider callback graph.</summary>
public class SelectedProviderResolutionBenchmarks : IDisposable
{
    private ProviderMeasurementContext _owner = null!;
    private NestedSetProviderCapabilities _expected = null!;

    /// <summary>Gets or sets the real provider profile.</summary>
    [Params("MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite")]
    public string Engine { get; set; } = "Sqlite";

    /// <summary>Gets or sets whether setup challenges weak runtime state with a complete collection.</summary>
    [Params(false, true)]
    public bool CollectBeforeMeasurement { get; set; }

    /// <summary>Initializes real services and optional collection outside measured resolution.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _owner = new ProviderMeasurementContext(Engine);
        _expected = NestedSetProviderCapabilities.Resolve(_owner.Context);
        if (CollectBeforeMeasurement)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        Validate();
    }

    /// <summary>Resolves the selected adapter without constructing queries or accessing a database.</summary>
    /// <returns>The actual retained adapter reference.</returns>
    [Benchmark]
    public object Resolve() => NestedSetProviderCapabilities.Resolve(_owner.Context);

    /// <summary>Checks exact owner identity and deferred connection state independently of observations.</summary>
    public void Validate()
    {
        if (!ReferenceEquals(_expected, Resolve())
            || _owner.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed
            || _owner.Context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Selected provider resolution changed ownership or performed I/O.");
        }
    }

    /// <summary>Validates and releases the independently owned provider graph.</summary>
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
