namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Demonstrates a classified rejection and verifies that it leaves the tree unchanged.</summary>
internal static class RejectedScenario
{
    /// <summary>Attempts to move an aggregate beneath its own sensor descendant.</summary>
    /// <param name="context">The fresh context owned by this scenario.</param>
    /// <param name="details">Whether the tree includes structural details.</param>
    /// <param name="cancellationToken">The token for every operation and output step.</param>
    /// <returns>A task that completes after the expected rejection and unchanged-state checks.</returns>
    public static async Task RunAsync(
        SignalLabContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(
            "REJECTED: an aggregate cannot become its descendant's child",
            cancellationToken);

        var data = await InstrumentDataset.CreateAsync(context, 4, "rejected", cancellationToken);
        var instruments = context
            .NestedSet<Instrument>()
            .ForScope(InstrumentDataset.ProjectId);

        await InstrumentScenarioSupport.PrintTreeAsync(
            instruments,
            data.TreeId,
            "Before the invalid move:",
            details,
            cancellationToken);

        var before = await instruments
            .InTree(data.TreeId)
            .Nodes
            .Select(instrument => new
            {
                instrument.NodeId,
                instrument.ProjectId,
                instrument.TreeId,
                instrument.ParentInstrumentId,
                instrument.Start,
                instrument.End,
                instrument.Level,
                instrument.SiblingPosition,
            })
            .ToListAsync(cancellationToken);

        var rejected = false;

        try
        {
            await instruments.MoveToAsync(data.ReceiverArray.NodeId, data.PrimarySensor.NodeId, cancellationToken);
        }
        catch (NestedSetException exception) when (exception.Code == NestedSetErrorCode.CycleDetected)
        {
            rejected = true;
            await SampleConsole.WriteLineAsync(
                $"Expected rejection: {exception.Code}. The station tree remains unchanged.",
                cancellationToken);
        }

        SampleConsole.Require(
            rejected,
            "Moving ReceiverArray beneath PrimarySensor should be rejected with CycleDetected.");

        var after = await instruments
            .InTree(data.TreeId)
            .Nodes
            .Select(instrument => new
            {
                instrument.NodeId,
                instrument.ProjectId,
                instrument.TreeId,
                instrument.ParentInstrumentId,
                instrument.Start,
                instrument.End,
                instrument.Level,
                instrument.SiblingPosition,
            })
            .ToListAsync(cancellationToken);

        SampleConsole.Require(
            before.SequenceEqual(after),
            "The rejected cycle should preserve every structural property.");

        await InstrumentScenarioSupport.PrintTreeAsync(
            instruments,
            data.TreeId,
            "After the rejected move:",
            details,
            cancellationToken);

        await InstrumentScenarioSupport.ValidateAsync(context, data, cancellationToken);
    }
}
