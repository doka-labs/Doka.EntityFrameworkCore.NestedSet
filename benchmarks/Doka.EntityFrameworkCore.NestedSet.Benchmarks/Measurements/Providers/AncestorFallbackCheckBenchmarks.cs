using Doka.EntityFrameworkCore.NestedSet.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes an already reported informational fallback check with ordinary logging enabled.</summary>
public class AncestorFallbackCheckBenchmarks : IDisposable
{
    private const string EventName = "Doka.EntityFrameworkCore.NestedSet.Query.AncestorIndexWithoutReader";
    private readonly List<EventData> _events = [];
    private QueryMeasurementModel _model = null!;

    /// <summary>Reports the first event before measuring the cached once-per-context decision.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _model = new QueryMeasurementModel(options => options.LogTo((eventId, _) => eventId.Name == EventName,
            _events.Add));
        Check();
        Validate();
    }

    /// <summary>Checks the actual once-marker without recreating its first event.</summary>
    [Benchmark]
    public void Check() => NestedSetQueryDiagnostics.AncestorIndexWithoutReader(_model.Context);

    /// <summary>Retains the once-per-context logging and no-I/O contracts without numeric gates.</summary>
    public void Validate()
    {
        Check();
        if (_events.Count != 1 || _events[0].EventId.Name != EventName
            || _model.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
        {
            throw new InvalidOperationException("The ancestor fallback check changed its event ownership.");
        }
    }

    /// <summary>Validates and releases independently owned context/provider state.</summary>
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
            _model?.Dispose();
        }
    }
}
