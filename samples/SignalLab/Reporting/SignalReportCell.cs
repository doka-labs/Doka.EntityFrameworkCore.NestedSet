namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Distinguishes an absent group from a measured mathematical zero.</summary>
/// <param name="AnchorId">The selected subtree anchor.</param>
/// <param name="ChannelId">The selected channel.</param>
/// <param name="Source">The selected measurement source.</param>
/// <param name="HasData">Whether SQL returned a qualifying group.</param>
/// <param name="Value">The aggregate, or the presentation zero for an absent group.</param>
internal sealed record SignalReportCell(
    Guid AnchorId,
    Guid ChannelId,
    CapturePort Source,
    bool HasData,
    double Value
);
