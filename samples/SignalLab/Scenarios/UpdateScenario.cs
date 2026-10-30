namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Demonstrates ordinary EF payload updates alongside explicitly managed hierarchy structure.</summary>
internal static class UpdateScenario
{
    /// <summary>Updates a tracked sensor counter and checks the recomputed SQL aggregates.</summary>
    /// <param name="context">The fresh context owned by this scenario.</param>
    /// <param name="details">Whether the tree includes structural details.</param>
    /// <param name="cancellationToken">The token for every operation and output step.</param>
    /// <returns>A task that completes after the ordinary SaveChanges update and its result checks.</returns>
    public static async Task RunAsync(
        SignalLabContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("UPDATE: ordinary DbSet payload changes", cancellationToken);

        var data = await InstrumentDataset.CreateAsync(context, 3, "update", cancellationToken);
        var instruments = context
            .NestedSet<Instrument>()
            .ForScope(InstrumentDataset.ProjectId);

        await InstrumentScenarioSupport.PrintTreeAsync(
            instruments,
            data.TreeId,
            "Before updating the sensor:",
            details,
            cancellationToken);

        var instrument = await context.Instruments.SingleAsync(
            node => node.NodeId == data.PrimarySensor.NodeId,
            cancellationToken);

        var originalPosition = instrument.SiblingPosition;
        var originalParent = instrument.ParentInstrumentId;

        instrument.Title = "Primary sensor v2";
        instrument.PulseCount = 150;
        await context.SaveChangesAsync(cancellationToken);

        await InstrumentScenarioSupport.PrintTreeAsync(
            instruments,
            data.TreeId,
            "After SaveChangesAsync:",
            details,
            cancellationToken);

        var persisted = await context
            .Instruments
            .AsNoTracking()
            .SingleAsync(node => node.NodeId == data.PrimarySensor.NodeId, cancellationToken);

        var receiverCount = await InstrumentScenarioSupport.SumLeafPulsesAsync(
            instruments,
            data.ReceiverArray.NodeId,
            cancellationToken);

        var totalCount = await InstrumentScenarioSupport.SumLeafPulsesAsync(
            instruments,
            data.Total.NodeId,
            cancellationToken);

        SampleConsole.Require(persisted.Title == "Primary sensor v2", "The renamed sensor should be persisted.");
        SampleConsole.Require(persisted.PulseCount == 150, "The sensor should persist 150 synthetic pulses.");
        SampleConsole.Require(
            persisted.SiblingPosition == originalPosition && persisted.ParentInstrumentId == originalParent,
            "A payload update should preserve the manually chosen parent and sibling position.");

        SampleConsole.Require(
            receiverCount == 195,
            $"Updated Receiver array should total 195 pulses, found {receiverCount}.");
        SampleConsole.Require(
            totalCount == 275,
            $"The updated station tree should total 275 pulses, found {totalCount}.");

        await SampleConsole.WriteLineAsync(
            $"Saved through context.Instruments and SaveChangesAsync: ReceiverArray={receiverCount}, total={totalCount}.",
            cancellationToken);

        await InstrumentScenarioSupport.ValidateAsync(context, data, cancellationToken);
    }
}
