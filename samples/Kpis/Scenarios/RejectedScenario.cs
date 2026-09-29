namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Demonstrates a classified rejection and verifies that it leaves the tree unchanged.</summary>
internal static class RejectedScenario
{
    /// <summary>Attempts to move an aggregate beneath its own service descendant.</summary>
    /// <param name="context">The fresh context owned by this scenario.</param>
    /// <param name="details">Whether the tree includes structural details.</param>
    /// <param name="cancellationToken">The token for every operation and output step.</param>
    /// <returns>A task that completes after the expected rejection and unchanged-state checks.</returns>
    public static async Task RunAsync(
        KpiContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(
            "REJECTED: an aggregate cannot become its descendant's child",
            cancellationToken);

        var data = await KpiDataset.CreateAsync(context, 4, "rejected", cancellationToken);
        var metrics = context
            .NestedSet<Kpi>()
            .ForScope(KpiDataset.ProjectId);

        await KpiScenarioSupport.PrintTreeAsync(
            metrics,
            data.TreeId,
            "Before the invalid move:",
            details,
            cancellationToken);

        var before = await metrics
            .InTree(data.TreeId)
            .Nodes
            .Select(metric => new
            {
                metric.NodeId,
                metric.ProjectId,
                metric.TreeId,
                metric.ParentMetricId,
                metric.Start,
                metric.End,
                metric.Level,
                metric.SiblingPosition,
            })
            .ToListAsync(cancellationToken);

        var rejected = false;

        try
        {
            await metrics.MoveToAsync(data.Platform.NodeId, data.Authentication.NodeId, cancellationToken);
        }
        catch (NestedSetException exception) when (exception.Code == NestedSetErrorCode.CycleDetected)
        {
            rejected = true;
            await SampleConsole.WriteLineAsync(
                $"Expected rejection: {exception.Code}. The reporting tree remains unchanged.",
                cancellationToken);
        }

        SampleConsole.Require(
            rejected,
            "Moving Platform beneath Authentication should be rejected with CycleDetected.");

        var after = await metrics
            .InTree(data.TreeId)
            .Nodes
            .Select(metric => new
            {
                metric.NodeId,
                metric.ProjectId,
                metric.TreeId,
                metric.ParentMetricId,
                metric.Start,
                metric.End,
                metric.Level,
                metric.SiblingPosition,
            })
            .ToListAsync(cancellationToken);

        SampleConsole.Require(
            before.SequenceEqual(after),
            "The rejected cycle should preserve every structural property.");

        await KpiScenarioSupport.PrintTreeAsync(
            metrics,
            data.TreeId,
            "After the rejected move:",
            details,
            cancellationToken);

        await KpiScenarioSupport.ValidateAsync(context, data, cancellationToken);
    }
}
