namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Demonstrates explicit sibling ordering without changing aggregate membership or measurements.</summary>
internal static class OrderScenario
{
    /// <summary>Moves Calibration array before receiver array and checks both positions and the unchanged leaf aggregate.</summary>
    /// <param name="context">The fresh context owned by this scenario.</param>
    /// <param name="details">Whether the tree includes structural details.</param>
    /// <param name="cancellationToken">The token for every operation and output step.</param>
    /// <returns>A task that completes after checking the reordered station tree.</returns>
    public static async Task RunAsync(
        SignalLabContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("ORDER: explicit sibling placement", cancellationToken);

        var data = await InstrumentDataset.CreateAsync(context, 2, "order", cancellationToken);
        var instruments = context
            .NestedSet<Instrument>()
            .ForScope(InstrumentDataset.ProjectId);

        await InstrumentScenarioSupport.PrintTreeAsync(
            instruments,
            data.TreeId,
            "Before reordering:",
            details,
            cancellationToken);

        // WHY: SignalLab presentation order is a domain choice; no Name/Title sorting criterion is configured.
        await instruments.MoveBeforeAsync(data.CalibrationArray.NodeId, data.ReceiverArray.NodeId, cancellationToken);

        await InstrumentScenarioSupport.PrintTreeAsync(
            instruments,
            data.TreeId,
            "After placing Calibration array before receiver array:",
            details,
            cancellationToken);

        var children = await instruments
            .ChildrenOf(data.Total.NodeId)
            .ToListAsync(cancellationToken);

        var totalCount = await InstrumentScenarioSupport.SumLeafPulsesAsync(
            instruments,
            data.Total.NodeId,
            cancellationToken);

        SampleConsole.Require(
            children
                .Select(instrument => instrument.Title)
                .SequenceEqual(["Calibration array", "Receiver array"]),
            "The root's child order should be Calibration array followed by receiver array.");

        SampleConsole.Require(
            children
                .Select(instrument => instrument.SiblingPosition)
                .SequenceEqual([0L, 1L]),
            "The reordered child positions should be contiguous: 0, 1.");

        SampleConsole.Require(totalCount == 245, $"Reordering should preserve the total of 245, found {totalCount}.");

        await SampleConsole.WriteLineAsync(
            $"Sibling positions: Calibration array=0, Receiver array=1; aggregate remains {totalCount}.",
            cancellationToken);

        await InstrumentScenarioSupport.ValidateAsync(context, data, cancellationToken);
    }
}
