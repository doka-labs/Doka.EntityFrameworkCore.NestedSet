namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Demonstrates server-side aggregates and independent Scope and TreeId selection.</summary>
internal static class AggregateScenario
{
    /// <summary>Checks the aggregate and exclusion of other station trees.</summary>
    /// <param name="context">The fresh context owned by this scenario.</param>
    /// <param name="details">Whether the tree includes structural details.</param>
    /// <param name="cancellationToken">The token for every operation and output step.</param>
    /// <returns>A task that completes after printing and checking the aggregate results.</returns>
    public static async Task RunAsync(
        SignalLabContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("AGGREGATE: one station tree, one project, one unit", cancellationToken);

        var data = await InstrumentDataset.CreateAsync(context, 1, "aggregate", cancellationToken);
        var instruments = context
            .NestedSet<Instrument>()
            .ForScope(InstrumentDataset.ProjectId);

        await InstrumentScenarioSupport.PrintTreeAsync(
            instruments,
            data.TreeId,
            "Station tree:",
            details,
            cancellationToken);

        // WHY: DescendantsOf resolves the anchor and its tree in SQL; no preliminary node load is needed.
        var receiverCount = await InstrumentScenarioSupport.SumLeafPulsesAsync(
            instruments,
            data.ReceiverArray.NodeId,
            cancellationToken);

        var totalCount = await InstrumentScenarioSupport.SumLeafPulsesAsync(
            instruments,
            data.Total.NodeId,
            cancellationToken);

        var selectedTreeCount = await instruments
            .TreeContaining(data.PrimarySensor.NodeId)
            .CountAsync(cancellationToken);

        var archivedCount = await instruments
            .InTree(data.RelatedTreeId)
            .Nodes
            .Where(instrument => instrument.End == instrument.Start + 1)
            .SumAsync(instrument => instrument.PulseCount ?? 0, cancellationToken);

        var otherProjectCount = await context
            .NestedSet<Instrument>()
            .ForScope(InstrumentDataset.OtherProjectId)
            .InTree(data.TreeId)
            .Nodes
            .Where(instrument => instrument.End == instrument.Start + 1)
            .SumAsync(instrument => instrument.PulseCount ?? 0, cancellationToken);

        SampleConsole.Require(receiverCount == 165, $"Receiver array should total 165 pulses, found {receiverCount}.");
        SampleConsole.Require(totalCount == 245, $"The station tree should total 245 pulses, found {totalCount}.");
        SampleConsole.Require(
            selectedTreeCount == 7,
            $"The selected tree should contain seven instruments, found {selectedTreeCount}.");
        SampleConsole.Require(
            archivedCount == 444,
            $"The separate reporting period should total 444, found {archivedCount}.");
        SampleConsole.Require(
            otherProjectCount == 999,
            $"The other project should total 999, found {otherProjectCount}.");

        await SampleConsole.WriteLineAsync(
            "Application rule: sum synthetic pulses from sensor leaves for one period.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            $"ReceiverArray: {receiverCount}; complete station tree: {totalCount}.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            $"Excluded: another tree in this project ({archivedCount}) "
            + $"and another project using the same TreeId ({otherProjectCount}).",
            cancellationToken);

        await InstrumentScenarioSupport.ValidateAsync(context, data, cancellationToken);
    }
}
