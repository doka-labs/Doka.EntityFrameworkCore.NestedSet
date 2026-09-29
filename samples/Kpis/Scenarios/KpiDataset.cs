namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Provides the deterministic anchors of one independently runnable KPI scenario.</summary>
/// <param name="TreeId">The primary reporting tree identity.</param>
/// <param name="RelatedTreeId">A second tree in the same project, excluded from primary-tree aggregates.</param>
/// <param name="Total">The reporting aggregate root.</param>
/// <param name="Platform">The branch containing two service measurements.</param>
/// <param name="Analytics">The sibling branch containing the reporting service.</param>
/// <param name="Authentication">The service used for payload changes and the cycle-rejection example.</param>
internal sealed record KpiDataset(
    Guid TreeId,
    Guid RelatedTreeId,
    Kpi Total,
    Kpi Platform,
    Kpi Analytics,
    Kpi Authentication
)
{
    /// <summary>Gets the application-owned project scope, without requiring a separate Project entity.</summary>
    public static Guid ProjectId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000001");

    /// <summary>Gets the unrelated project scope used to demonstrate partition isolation.</summary>
    public static Guid OtherProjectId { get; } = Guid.Parse("20000000-0000-0000-0000-000000000001");

    /// <summary>Builds the reporting tree and two exclusion examples through the public facade.</summary>
    /// <param name="context">The scenario-owned context connected to the initialized sample database.</param>
    /// <param name="scenarioNumber">The fixed identity prefix, unique across the four selectable scenarios.</param>
    /// <param name="scenarioName">The label retained in the persisted roots for later inspection.</param>
    /// <param name="cancellationToken">The token for every hierarchy and database operation.</param>
    /// <returns>The inserted anchors used by the scenario.</returns>
    public static async Task<KpiDataset> CreateAsync(
        KpiContext context,
        int scenarioNumber,
        string scenarioName,
        CancellationToken cancellationToken
    )
    {
        var treeId = CreateId(scenarioNumber, 100);
        var relatedTreeId = CreateId(scenarioNumber, 101);
        var metrics = context
            .NestedSet<Kpi>()
            .ForScope(ProjectId);

        var total = CreateMetric(scenarioNumber, 1, $"Successful deployments [{scenarioName}]");
        var platform = CreateMetric(scenarioNumber, 2, "Platform");
        var identity = CreateMetric(scenarioNumber, 3, "Identity");
        var authentication = CreateMetric(scenarioNumber, 4, "Authentication", 120);
        var permissions = CreateMetric(scenarioNumber, 5, "Permissions", 45);
        var analytics = CreateMetric(scenarioNumber, 6, "Analytics");
        var reporting = CreateMetric(scenarioNumber, 7, "Reporting", 80);

        await metrics.InsertRootAsync(total, treeId, cancellationToken);
        await metrics.InsertChildAsync(platform, total.NodeId, cancellationToken);
        await metrics.InsertChildAsync(identity, platform.NodeId, cancellationToken);
        await metrics.InsertChildAsync(authentication, identity.NodeId, cancellationToken);
        await metrics.InsertChildAsync(permissions, identity.NodeId, cancellationToken);
        await metrics.InsertChildAsync(analytics, total.NodeId, cancellationToken);
        await metrics.InsertChildAsync(reporting, analytics.NodeId, cancellationToken);

        var relatedRoot = CreateMetric(scenarioNumber, 8, $"Archived reporting [{scenarioName}]");
        await metrics.InsertRootAsync(relatedRoot, relatedTreeId, cancellationToken);
        await metrics.InsertChildAsync(
            CreateMetric(scenarioNumber, 9, "Unrelated reporting period", 444),
            relatedRoot.NodeId,
            cancellationToken);

        var otherProject = context
            .NestedSet<Kpi>()
            .ForScope(OtherProjectId);

        var otherRoot = CreateMetric(scenarioNumber, 10, $"Other project [{scenarioName}]");

        // WHY: Reusing TreeId in another ProjectId makes the complete (Scope, TreeId) identity visible.
        await otherProject.InsertRootAsync(otherRoot, treeId, cancellationToken);
        await otherProject.InsertChildAsync(
            CreateMetric(scenarioNumber, 11, "Other deployment pipeline", 999),
            otherRoot.NodeId,
            cancellationToken);

        return new KpiDataset(treeId, relatedTreeId, total, platform, analytics, authentication);
    }

    /// <summary>Creates a service or aggregate with an application-assigned, repeatable identity.</summary>
    /// <param name="scenarioNumber">The scenario's unique identity prefix.</param>
    /// <param name="ordinal">The metric number inside the scenario.</param>
    /// <param name="title">The service or aggregate label.</param>
    /// <param name="deployments">The leaf count, or null for an aggregate.</param>
    /// <returns>A detached metric ready for explicit insertion.</returns>
    private static Kpi CreateMetric(
        int scenarioNumber,
        byte ordinal,
        string title,
        int? deployments = null
    ) => new()
    {
        NodeId = CreateId(scenarioNumber, ordinal),
        Title = title,
        SuccessfulDeployments = deployments,
    };

    /// <summary>Produces fixed sample identities without implying a production key-generation policy.</summary>
    /// <param name="scenarioNumber">The scenario prefix stored in the first identity segment.</param>
    /// <param name="ordinal">The sample-local identity number.</param>
    /// <returns>A deterministic identity for the small, fixed sample dataset.</returns>
    private static Guid CreateId(
        int scenarioNumber,
        byte ordinal
    ) // WHY: Fixed sample identities reproduce detailed reset output; production keys may use Guid.NewGuid.
        => new(scenarioNumber, 0, 0, 0, 0, 0, 0, 0, 0, 0, ordinal);
}
