namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Identifies independent physical acquisition formats in the synthetic laboratory.</summary>
internal enum CapturePort
{
    /// <summary>Reads native pulse-counter events.</summary>
    Pulse,

    /// <summary>Reads completed receiver integration windows.</summary>
    Sweep,

    /// <summary>Reads device-specific burst magnitudes.</summary>
    Burst,
}
