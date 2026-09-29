namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Demonstrates ordinary EF payload updates alongside explicitly managed hierarchy structure.</summary>
internal static class UpdateScenario
{
    /// <summary>Updates a tracked service measurement and checks the recomputed SQL aggregates.</summary>
    /// <param name="context">The fresh context owned by this scenario.</param>
    /// <param name="details">Whether the tree includes structural details.</param>
    /// <param name="cancellationToken">The token for every operation and output step.</param>
    /// <returns>A task that completes after the ordinary SaveChanges update and its result checks.</returns>
    public static async Task RunAsync(
        KpiContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("UPDATE: ordinary DbSet payload changes", cancellationToken);

        var data = await KpiDataset.CreateAsync(context, 3, "update", cancellationToken);
        var metrics = context
            .NestedSet<Kpi>()
            .ForScope(KpiDataset.ProjectId);

        await KpiScenarioSupport.PrintTreeAsync(
            metrics,
            data.TreeId,
            "Before updating the service:",
            details,
            cancellationToken);

        var metric = await context.Kpis.SingleAsync(
            node => node.NodeId == data.Authentication.NodeId,
            cancellationToken);

        var originalPosition = metric.SiblingPosition;
        var originalParent = metric.ParentMetricId;

        metric.Title = "Authentication v2";
        metric.SuccessfulDeployments = 150;
        await context.SaveChangesAsync(cancellationToken);

        await KpiScenarioSupport.PrintTreeAsync(
            metrics,
            data.TreeId,
            "After SaveChangesAsync:",
            details,
            cancellationToken);

        var persisted = await context
            .Kpis
            .AsNoTracking()
            .SingleAsync(node => node.NodeId == data.Authentication.NodeId, cancellationToken);

        var platformCount = await KpiScenarioSupport.SumLeafDeploymentsAsync(
            metrics,
            data.Platform.NodeId,
            cancellationToken);

        var totalCount = await KpiScenarioSupport.SumLeafDeploymentsAsync(
            metrics,
            data.Total.NodeId,
            cancellationToken);

        SampleConsole.Require(persisted.Title == "Authentication v2", "The renamed service should be persisted.");
        SampleConsole.Require(persisted.SuccessfulDeployments == 150, "The service should persist 150 successful deployments.");
        SampleConsole.Require(
            persisted.SiblingPosition == originalPosition && persisted.ParentMetricId == originalParent,
            "A payload update should preserve the manually chosen parent and sibling position.");

        SampleConsole.Require(platformCount == 195, $"Updated Platform should total 195 deployments, found {platformCount}.");
        SampleConsole.Require(totalCount == 275, $"The updated reporting tree should total 275 deployments, found {totalCount}.");

        await SampleConsole.WriteLineAsync(
            $"Saved through context.Kpis and SaveChangesAsync: Platform={platformCount}, total={totalCount}.",
            cancellationToken);

        await KpiScenarioSupport.ValidateAsync(context, data, cancellationToken);
    }
}
