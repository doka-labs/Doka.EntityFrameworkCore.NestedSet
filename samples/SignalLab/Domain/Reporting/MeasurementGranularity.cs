namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Distinguishes the synthetic transport's daily samples from coarse acquisition batches.</summary>
public enum MeasurementGranularity
{
    /// <summary>Captures one dated acquisition sample.</summary>
    Daily,

    /// <summary>Captures a coarse batch that cannot contribute to the daily sample grain.</summary>
    Monthly,
}
