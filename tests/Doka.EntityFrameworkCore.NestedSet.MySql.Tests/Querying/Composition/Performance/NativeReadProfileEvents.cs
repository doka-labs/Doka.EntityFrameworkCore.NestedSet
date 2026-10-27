using System.Diagnostics.Tracing;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Marks optional diagnostic read windows without recording SQL, identities or request values.</summary>
[EventSource(Name = "NestedSet.AllocationProfile")]
internal sealed class NativeReadProfileEvents : EventSource
{
    /// <summary>Gets the process-local marker source used by native allocation profiling.</summary>
    internal static NativeReadProfileEvents Log { get; } = new();

    /// <summary>Keeps the event source process-local and prevents additional marker identities.</summary>
    private NativeReadProfileEvents() { }

    /// <summary>Emits an optional start marker before the allocation and timing counters begin.</summary>
    [NonEvent]
    internal void BeginRead(
        string engine,
        string profile,
        string phase,
        int sample
    )
    {
        if (IsEnabled())
        {
            ReadStarted(engine, profile, phase, sample);
        }
    }

    /// <summary>Emits an optional completion marker after the allocation and timing counters stop.</summary>
    [NonEvent]
    internal void EndRead(
        string engine,
        string profile,
        string phase,
        int sample
    )
    {
        if (IsEnabled())
        {
            ReadCompleted(engine, profile, phase, sample);
        }
    }

    /// <summary>Identifies one observed or observer-light complete read in an opt-in diagnostic trace.</summary>
    /// <param name="engine">The fixed provider engine name.</param>
    /// <param name="profile">The fixed fixture profile, without application values.</param>
    /// <param name="phase">The observed or observer-light attribution window.</param>
    /// <param name="sample">The zero-based measured sample number.</param>
    [Event(1, Level = EventLevel.Informational)]
    public void ReadStarted(
        string engine,
        string profile,
        string phase,
        int sample
    ) => WriteEvent(1, engine, profile, phase, sample);

    /// <summary>Closes one observed or observer-light complete read in an opt-in diagnostic trace.</summary>
    /// <param name="engine">The fixed provider engine name.</param>
    /// <param name="profile">The fixed fixture profile, without application values.</param>
    /// <param name="phase">The observed or observer-light attribution window.</param>
    /// <param name="sample">The zero-based measured sample number.</param>
    [Event(2, Level = EventLevel.Informational)]
    public void ReadCompleted(
        string engine,
        string profile,
        string phase,
        int sample
    ) => WriteEvent(2, engine, profile, phase, sample);
}
