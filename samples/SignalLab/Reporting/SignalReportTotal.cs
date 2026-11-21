namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Stores one scalar SQL aggregate, retaining source as part of its grain.</summary>
internal sealed class SignalReportTotal
{
    /// <summary>Gets the subtree anchor identity.</summary>
    public Guid AnchorId { get; init; }

    /// <summary>Gets the channel identity.</summary>
    public Guid ChannelId { get; init; }

    /// <summary>Gets the independent physical source.</summary>
    public CapturePort Source { get; init; }

    /// <summary>Gets the sum of qualifying measurement rows.</summary>
    public double Value { get; init; }
}
