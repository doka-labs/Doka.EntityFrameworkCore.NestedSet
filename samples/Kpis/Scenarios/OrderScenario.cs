namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Demonstrates explicit sibling ordering without changing aggregate membership or measurements.</summary>
internal static class OrderScenario
{
    /// <summary>Moves Analytics before Platform and checks both positions and the unchanged leaf aggregate.</summary>
    /// <param name="context">The fresh context owned by this scenario.</param>
    /// <param name="details">Whether the tree includes structural details.</param>
    /// <param name="cancellationToken">The token for every operation and output step.</param>
    /// <returns>A task that completes after checking the reordered reporting tree.</returns>
    public static async Task RunAsync(
        KpiContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("ORDER: explicit sibling placement", cancellationToken);

        var data = await KpiDataset.CreateAsync(context, 2, "order", cancellationToken);
        var metrics = context
            .NestedSet<Kpi>()
            .ForScope(KpiDataset.ProjectId);

        await KpiScenarioSupport.PrintTreeAsync(metrics, data.TreeId, "Before reordering:", details, cancellationToken);

        // WHY: KPI presentation order is a domain choice; no Name/Title sorting criterion is configured.
        await metrics.MoveBeforeAsync(data.Analytics.NodeId, data.Platform.NodeId, cancellationToken);

        await KpiScenarioSupport.PrintTreeAsync(
            metrics,
            data.TreeId,
            "After placing Analytics before Platform:",
            details,
            cancellationToken);

        var children = await metrics
            .ChildrenOf(data.Total.NodeId)
            .ToListAsync(cancellationToken);

        var totalCount = await KpiScenarioSupport.SumLeafDeploymentsAsync(
            metrics,
            data.Total.NodeId,
            cancellationToken);

        SampleConsole.Require(
            children
                .Select(metric => metric.Title)
                .SequenceEqual(["Analytics", "Platform"]),
            "The root's child order should be Analytics followed by Platform.");

        SampleConsole.Require(
            children
                .Select(metric => metric.SiblingPosition)
                .SequenceEqual([0L, 1L]),
            "The reordered child positions should be contiguous: 0, 1.");

        SampleConsole.Require(totalCount == 245, $"Reordering should preserve the total of 245, found {totalCount}.");

        await SampleConsole.WriteLineAsync(
            $"Sibling positions: Analytics=0, Platform=1; aggregate remains {totalCount}.",
            cancellationToken);

        await KpiScenarioSupport.ValidateAsync(context, data, cancellationToken);
    }
}
