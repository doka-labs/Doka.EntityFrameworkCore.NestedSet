namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Demonstrates server-side aggregates and independent Scope and TreeId selection.</summary>
internal static class AggregateScenario
{
    /// <summary>Checks the aggregate and exclusion of other reporting trees.</summary>
    /// <param name="context">The fresh context owned by this scenario.</param>
    /// <param name="details">Whether the tree includes structural details.</param>
    /// <param name="cancellationToken">The token for every operation and output step.</param>
    /// <returns>A task that completes after printing and checking the aggregate results.</returns>
    public static async Task RunAsync(
        KpiContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("AGGREGATE: one reporting tree, one project, one unit", cancellationToken);

        var data = await KpiDataset.CreateAsync(context, 1, "aggregate", cancellationToken);
        var metrics = context
            .NestedSet<Kpi>()
            .ForScope(KpiDataset.ProjectId);

        await KpiScenarioSupport.PrintTreeAsync(metrics, data.TreeId, "Reporting tree:", details, cancellationToken);

        // WHY: DescendantsOf resolves the anchor and its tree in SQL; no preliminary node load is needed.
        var platformCount = await KpiScenarioSupport.SumLeafDeploymentsAsync(
            metrics,
            data.Platform.NodeId,
            cancellationToken);

        var totalCount = await KpiScenarioSupport.SumLeafDeploymentsAsync(
            metrics,
            data.Total.NodeId,
            cancellationToken);

        var selectedTreeCount = await metrics
            .TreeContaining(data.Authentication.NodeId)
            .CountAsync(cancellationToken);

        var archivedCount = await metrics
            .InTree(data.RelatedTreeId)
            .Nodes
            .Where(metric => metric.End == metric.Start + 1)
            .SumAsync(metric => metric.SuccessfulDeployments ?? 0, cancellationToken);

        var otherProjectCount = await context
            .NestedSet<Kpi>()
            .ForScope(KpiDataset.OtherProjectId)
            .InTree(data.TreeId)
            .Nodes
            .Where(metric => metric.End == metric.Start + 1)
            .SumAsync(metric => metric.SuccessfulDeployments ?? 0, cancellationToken);

        SampleConsole.Require(platformCount == 165, $"Platform should total 165 deployments, found {platformCount}.");
        SampleConsole.Require(totalCount == 245, $"The reporting tree should total 245 deployments, found {totalCount}.");
        SampleConsole.Require(selectedTreeCount == 7, $"The selected tree should contain seven metrics, found {selectedTreeCount}.");
        SampleConsole.Require(archivedCount == 444, $"The separate reporting period should total 444, found {archivedCount}.");
        SampleConsole.Require(otherProjectCount == 999, $"The other project should total 999, found {otherProjectCount}.");

        await SampleConsole.WriteLineAsync(
            "Application rule: sum successful deployments from service leaves for one period.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            $"Platform: {platformCount}; complete reporting tree: {totalCount}.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            $"Excluded: another tree in this project ({archivedCount}) "
            + $"and another project using the same TreeId ({otherProjectCount}).",
            cancellationToken);

        await KpiScenarioSupport.ValidateAsync(context, data, cancellationToken);
    }
}
