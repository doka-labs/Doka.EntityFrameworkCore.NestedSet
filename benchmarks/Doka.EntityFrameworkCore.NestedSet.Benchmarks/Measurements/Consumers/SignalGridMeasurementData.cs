using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Prepares the existing dense and late-failing sparse consumer grains without measured setup work.</summary>
internal static class SignalGridMeasurementData
{
    /// <summary>Creates the independently selected anchor/channel/source axes.</summary>
    /// <param name="anchors">The number of selected anchors.</param>
    /// <param name="channels">The number of selected channels.</param>
    /// <returns>The actual validated report request.</returns>
    internal static ValidatedSignalReport Request(
        int anchors,
        int channels
    ) => SignalReportValidation.Snapshot(new SignalReportRequest(
        SignalReportDataset.Id(1000),
        SignalReportDataset.Id(1001),
        Enumerable.Range(1, anchors).Select(SignalReportDataset.Id).ToArray(),
        Enumerable.Range(101, channels).Select(SignalReportDataset.Id).ToArray(),
        [CapturePort.Sweep, CapturePort.Pulse, CapturePort.Burst],
        SignalReportDataset.FirstDay,
        SignalReportDataset.FirstDay.AddDays(2),
        MeasurementGranularity.Daily,
        []));

    /// <summary>Builds one exact scalar input without substituting materializer behavior.</summary>
    internal static SignalReportTotal Total(
        Guid anchor,
        Guid channel,
        CapturePort source,
        double value
    ) => new()
    {
        AnchorId = anchor,
        ChannelId = channel,
        Source = source,
        Value = value,
    };
}
