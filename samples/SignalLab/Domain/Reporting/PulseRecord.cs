namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Stores one pulse-port event in its native counter format.</summary>
public sealed class PulseRecord
{
    /// <summary>Gets the storage-assigned event identity, independent of the event's measured value.</summary>
    public long Sequence { get; private set; }

    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the synthetic signal channel.</summary>
    public Guid ChannelId { get; init; }

    /// <summary>Gets the measurement owner.</summary>
    public Guid NodeId { get; init; }

    /// <summary>Gets the explicitly selected measurement grain.</summary>
    public MeasurementGranularity Granularity { get; init; }

    /// <summary>Gets the application-assigned measurement day.</summary>
    public DateOnly Day { get; init; }

    /// <summary>Gets the additive pulse counter emitted by this event.</summary>
    public double PulseCount { get; init; }
}
