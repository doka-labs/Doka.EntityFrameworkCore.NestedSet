namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Captures actual library instruments while excluding other concurrently executing test flows.</summary>
internal sealed class DiagnosticsCapture : IDisposable
{
    private static readonly AsyncLocal<object?> s_owner = new();
    private readonly object _owner = new();
    private readonly object? _previous;
    private readonly ActivityListener _activities;
    private readonly MeterListener _metrics;

    /// <summary>Starts capture for the current asynchronous test flow.</summary>
    internal DiagnosticsCapture()
    {
        _previous = s_owner.Value;
        s_owner.Value = _owner;
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NestedSetDiagnostics.ActivitySourceName,
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (ReferenceEquals(s_owner.Value, _owner))
                {
                    Activities.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(_activities);
        _metrics = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == NestedSetDiagnostics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        _metrics.SetMeasurementEventCallback<double>((
            instrument,
            value,
            tags,
            _
        ) => Record(instrument, value, tags));

        _metrics.SetMeasurementEventCallback<long>((
            instrument,
            value,
            tags,
            _
        ) => Record(instrument, value, tags));
        _metrics.Start();
    }

    /// <summary>Gets stopped activities from the owning test's execution flow.</summary>
    internal List<Activity> Activities { get; } = [];

    /// <summary>Gets measurements from the owning test's execution flow.</summary>
    internal List<Measurement> Measurements { get; } = [];

    /// <summary>Stops both real listeners and restores the preceding execution-flow owner.</summary>
    public void Dispose()
    {
        _metrics.Dispose();
        _activities.Dispose();
        s_owner.Value = _previous;
    }

    private void Record(
        Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags
    )
    {
        // WHY: Listener subscription is process-wide, but parallel test data must not influence this case's counts.
        if (ReferenceEquals(s_owner.Value, _owner))
        {
            Measurements.Add(new Measurement(instrument.Name, value, tags.ToArray()));
        }
    }

    /// <summary>Retains the emitted instrument value and exact tags for count and privacy assertions.</summary>
    /// <param name="Name">The published instrument name.</param>
    /// <param name="Value">The emitted numeric value.</param>
    /// <param name="Tags">The complete tags emitted with this measurement.</param>
    internal sealed record Measurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags
    );
}
