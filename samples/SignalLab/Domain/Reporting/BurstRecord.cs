namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Stores one burst-port event in its device-specific acquisition format.</summary>
public sealed class BurstRecord
{
    /// <summary>Gets the storage-assigned event identity within the burst input relation.</summary>
    public long Sequence { get; private set; }

    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the signal mapped to a synthetic signal channel.</summary>
    public Guid SignalId { get; init; }

    /// <summary>Gets the emitting device represented by an instrument node.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>Gets the explicitly selected measurement grain.</summary>
    public MeasurementGranularity BucketSize { get; init; }

    /// <summary>Gets the date recorded by the burst transport.</summary>
    public DateOnly CapturedOn { get; init; }

    /// <summary>Gets the additive magnitude of this synthetic burst event.</summary>
    public double Magnitude { get; init; }
}
