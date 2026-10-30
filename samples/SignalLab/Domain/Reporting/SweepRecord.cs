namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Stores one sweep-port event using its receiver and integration-window fields.</summary>
public sealed class SweepRecord
{
    /// <summary>Gets the storage-assigned event identity within the sweep input relation.</summary>
    public long Sequence { get; private set; }

    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the stream mapped to a synthetic signal channel.</summary>
    public Guid StreamId { get; init; }

    /// <summary>Gets the receiver represented by a node in the instrument hierarchy.</summary>
    public Guid ReceiverId { get; init; }

    /// <summary>Gets the explicitly selected measurement grain.</summary>
    public MeasurementGranularity Resolution { get; init; }

    /// <summary>Gets the date on which this acquisition window completed.</summary>
    public DateOnly CompletedOn { get; init; }

    /// <summary>Gets the synthetic ticks integrated over this acquisition window.</summary>
    public double IntegratedTicks { get; init; }
}
