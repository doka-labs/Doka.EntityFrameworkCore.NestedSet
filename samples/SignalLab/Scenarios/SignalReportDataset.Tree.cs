namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal sealed partial record SignalReportDataset
{
    /// <summary>Creates detached payload with no public structural coordinates.</summary>
    private static SignalNode Node(
        Guid tenant,
        int ordinal,
        Guid type,
        string name
    ) => new()
    {
        TenantId = tenant,
        Id = Id(ordinal),
        TypeId = type,
        Name = name,
    };

    /// <summary>Constructs the chosen topology without changing the database or retaining query state.</summary>
    private static NestedSetBranch<SignalNode> CreateBranch(
        Guid tenant,
        bool nearest,
        string scenarioName
    )
    {
        NestedSetBranch<SignalNode> branch;

        if (nearest)
        {
            var sensorP = new NestedSetBranch<SignalNode>(Node(tenant, 6, SensorKind, "Sensor P"));
            var sensorQ = new NestedSetBranch<SignalNode>(Node(tenant, 5, SensorKind, "Sensor Q"), [sensorP]);

            var relay = new NestedSetBranch<SignalNode>(Node(tenant, 4, ModuleKind, "Relay module"), [sensorQ]);

            var receiver = new NestedSetBranch<SignalNode>(Node(tenant, 3, ModuleKind, "Receiver module"), [relay]);

            branch = new NestedSetBranch<SignalNode>(
                Node(tenant, 1, StationKind, $"Station [{scenarioName}]"),
                [receiver]);
        }
        else
        {
            var module = new NestedSetBranch<SignalNode>(
                Node(tenant, 3, ModuleKind, "Receiver module"),
                [
                    new NestedSetBranch<SignalNode>(Node(tenant, 4, SensorKind, "Sensor A")),
                    new NestedSetBranch<SignalNode>(Node(tenant, 5, SensorKind, "Sensor B")),
                ]);

            var array = new NestedSetBranch<SignalNode>(Node(tenant, 2, ArrayKind, "Narrow-band array"), [module]);
            branch = new NestedSetBranch<SignalNode>(
                Node(tenant, 1, StationKind, $"Station [{scenarioName}]"),
                [array]);
        }

        return branch;
    }

    /// <summary>Assigns the same inclusive edge periods before any scenario measurements are saved.</summary>
    private static void AddLinkWindows(
        SignalLabContext context,
        Guid tenant,
        bool nearest
    )
    {
        (int, int)[] edges = nearest
            ? [(1, 3), (3, 4), (4, 5), (5, 6)]
            : [(1, 2), (2, 3), (3, 4), (3, 5)];

        foreach (var (parent, child) in edges)
        {
            SignalReportValidation.AddPeriod(
                context,
                new LinkWindow
                {
                    TenantId = tenant,
                    ParentId = Id(parent),
                    ChildId = Id(child),
                    FromDate = new DateOnly(2042, 1, 1),
                    ToDate = !nearest && child == 5
                        ? FirstDay
                        : null,
                });
        }
    }
}
