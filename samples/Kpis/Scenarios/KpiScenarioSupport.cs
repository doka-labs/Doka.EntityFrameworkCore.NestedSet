namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Shares only the display, aggregate and validation steps used by multiple KPI scenarios.</summary>
internal static class KpiScenarioSupport
{
    /// <summary>Sums service-leaf deployments in the database without loading the aggregate anchor.</summary>
    /// <param name="metrics">The facade bound to the reporting project.</param>
    /// <param name="branchId">The aggregate whose strict descendants contribute measurements.</param>
    /// <param name="cancellationToken">The token for the SQL aggregate.</param>
    /// <returns>The successful-deployment count, with missing leaf measurements treated as zero.</returns>
    public static Task<int> SumLeafDeploymentsAsync(
        ScopedNestedSet<Kpi, Guid> metrics,
        Guid branchId,
        CancellationToken cancellationToken
    ) // WHY: All service leaves measure the same unit and period; summing aggregate branches would double-count them.
        => metrics
            .DescendantsOf(branchId)
            .Where(metric => metric.End == metric.Start + 1)
            .SumAsync(metric => metric.SuccessfulDeployments ?? 0, cancellationToken);

    /// <summary>Prints one small tree in hierarchy preorder, with optional structural details.</summary>
    /// <param name="metrics">The facade bound to exactly one project.</param>
    /// <param name="treeId">The tree identity inside the bound project.</param>
    /// <param name="heading">The label identifying the displayed state.</param>
    /// <param name="details">Whether to include the maintained coordinates and identities.</param>
    /// <param name="cancellationToken">The token for reads and console output.</param>
    /// <returns>A task that completes when the tree has been printed.</returns>
    public static async Task PrintTreeAsync(
        ScopedNestedSet<Kpi, Guid> metrics,
        Guid treeId,
        string heading,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(heading, cancellationToken);

        var nodes = await metrics
            .InTree(treeId)
            .Nodes
            .ToListAsync(cancellationToken);

        foreach (var metric in nodes)
        {
            var measurement = metric.SuccessfulDeployments is { } count ? $" = {count}" : string.Empty;
            var structure = details
                ? $" [Id={metric.NodeId}, TreeId={metric.TreeId}, Depth={metric.Level}, "
                + $"Position={metric.SiblingPosition}, Bounds={metric.Start}..{metric.End}]"
                : string.Empty;

            await SampleConsole.WriteLineAsync(
                $"{new string(' ', metric.Level * 2)}{metric.Title}{measurement}{structure}",
                cancellationToken);
        }
    }

    /// <summary>Inspects persisted results without migrating, seeding or changing the database.</summary>
    /// <param name="context">The inspection context, opened read-only when using SQLite.</param>
    /// <param name="details">Whether to include structural details.</param>
    /// <param name="cancellationToken">The token for database reads and output.</param>
    /// <returns>A task that completes after every retained reporting tree has been printed.</returns>
    public static async Task InspectAsync(
        KpiContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        var roots = await context
            .Kpis
            .AsNoTracking()
            .Where(metric => metric.ParentMetricId == null)
            .OrderBy(metric => metric.Title)
            .Select(metric => new
            {
                metric.ProjectId,
                metric.TreeId,
                metric.Title,
            })
            .ToListAsync(cancellationToken);

        if (roots.Count == 0)
        {
            await SampleConsole.WriteLineAsync("The KPI sample has no persisted reporting trees.", cancellationToken);

            return;
        }

        foreach (var root in roots)
        {
            var project = root.ProjectId == KpiDataset.ProjectId ? "main project" : "other project";
            var metrics = context
                .NestedSet<Kpi>()
                .ForScope(root.ProjectId);

            await PrintTreeAsync(metrics, root.TreeId, $"{root.Title} ({project})", details, cancellationToken);
        }
    }

    /// <summary>Validates all trees belonging to a scenario, including its two isolation examples.</summary>
    /// <param name="context">The scenario-owned context.</param>
    /// <param name="data">The persisted scenario identities.</param>
    /// <param name="cancellationToken">The token for full validation.</param>
    /// <returns>A task that completes after all trees satisfy the maintained invariants.</returns>
    /// <exception cref="InvalidOperationException">A tree violates its structural contract.</exception>
    public static async Task ValidateAsync(
        KpiContext context,
        KpiDataset data,
        CancellationToken cancellationToken
    )
    {
        var trees = new[]
        {
            (ProjectId: KpiDataset.ProjectId, data.TreeId),
            (ProjectId: KpiDataset.ProjectId, TreeId: data.RelatedTreeId),
            (ProjectId: KpiDataset.OtherProjectId, data.TreeId),
        };

        foreach (var tree in trees)
        {
            var report = await context
                .NestedSet<Kpi>()
                .ForScope(tree.ProjectId)
                .InTree(tree.TreeId)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

            SampleConsole.Require(
                report.IsValid,
                $"Tree {tree.TreeId} in project {tree.ProjectId} failed validation: "
                + string.Join("; ", report.Issues.Select(issue => issue.Message)));
        }
    }
}
