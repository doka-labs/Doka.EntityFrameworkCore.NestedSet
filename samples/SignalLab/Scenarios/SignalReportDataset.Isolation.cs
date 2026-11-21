namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal sealed partial record SignalReportDataset
{
    /// <summary>Stores deliberate lookalike facts in another tree and another tenant with the same TreeId.</summary>
    private static async Task SeedIsolationAsync(
        SignalLabContext context,
        Guid tenant,
        Guid tree,
        int scenario,
        CancellationToken cancellationToken
    )
    {
        var hierarchy = context
            .NestedSet<SignalNode>()
            .ForScope(tenant);

        await hierarchy.InsertForestAsync(
            [new NestedSetTreeImport<SignalNode, Guid>(Id(4000 + scenario), IsolationBranch(tenant, 10, "Other tree"))],
            cancellationToken);

        (int, int)[] otherTreeEdges = [(10, 11), (11, 12), (12, 13), (12, 14)];

        foreach (var (parent, child) in otherTreeEdges)
        {
            SignalReportValidation.AddPeriod(
                context,
                new LinkWindow
                {
                    TenantId = tenant,
                    ParentId = Id(parent),
                    ChildId = Id(child),
                    FromDate = DateOnly.MinValue,
                });
        }

        AddMeasurements(context, tenant, Id(13), FirstDay, 444);
        await context.SaveChangesAsync(cancellationToken);

        // WHY: Each dataset invocation owns this extra scope. Scenario numbers recur when
        // caller-owned main tenants share a database, so a scenario-derived key would alias catalogs.
        var otherTenant = Guid.NewGuid();
        await SeedCatalogAsync(context, otherTenant, cancellationToken);
        await context
            .NestedSet<SignalNode>()
            .ForScope(otherTenant)
            .InsertForestAsync(
                [new NestedSetTreeImport<SignalNode, Guid>(tree, IsolationBranch(otherTenant, 1, "Other tenant"))],
                cancellationToken);

        // WHY: Identical root bounds and repeated node keys make scope and tree isolation observable without accidental
        // range differences.
        (int, int)[] otherTenantEdges = [(1, 2), (2, 3), (3, 4), (3, 5)];

        foreach (var (parent, child) in otherTenantEdges)
        {
            SignalReportValidation.AddPeriod(
                context,
                new LinkWindow
                {
                    TenantId = otherTenant,
                    ParentId = Id(parent),
                    ChildId = Id(child),
                    FromDate = DateOnly.MinValue,
                });
        }

        AddMeasurements(context, otherTenant, Id(4), FirstDay, 999);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// Creates an isolation station without a matching module owner.
    /// </summary>
    private static NestedSetBranch<SignalNode> IsolationBranch(
        Guid tenant,
        int root,
        string name
    )
    {
        var module = new NestedSetBranch<SignalNode>(
            Node(tenant, root + 2, StationKind, $"{name} module"),
            [
                new NestedSetBranch<SignalNode>(Node(tenant, root + 3, SensorKind, $"{name} sensor A")),
                new NestedSetBranch<SignalNode>(Node(tenant, root + 4, SensorKind, $"{name} sensor B")),
            ]);

        var array = new NestedSetBranch<SignalNode>(Node(tenant, root + 1, StationKind, $"{name} array"), [module]);

        return new NestedSetBranch<SignalNode>(Node(tenant, root, StationKind, name), [array]);
    }
}
