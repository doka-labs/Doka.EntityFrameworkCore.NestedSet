using GroupApp = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;
using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Holds only bounded consumer payload identities, never the persisted structural population.</summary>
/// <param name="Nodes">The existing structural population, retained without a CLR entity buffer.</param>
/// <param name="Deep">Whether sparse payload was chosen for the shared hundred-thousand-depth profile.</param>
/// <param name="Facts">The bounded, deliberately selected measurement owners.</param>
internal sealed partial record CompositionConsumerDataset(
    int Nodes,
    bool Deep,
    int[] Facts
)
{
    /// <summary>Names the fixed case-sensitive fields combined on one actual signal owner.</summary>
    internal static readonly string[] SignalFields = ["Band", "ModuleClass", "Cooling", "RackCode"];

    /// <summary>Names the actual sample's sixteen permitted routing fields.</summary>
    internal static readonly string[] GroupFields =
    [
        "Route", "ChannelClass", "ManagerRoute", "RoutingZone", "Lane", "Cluster", "Protocol", "Mode",
        "Profile", "Phase", "Zone", "Port", "Rack", "Batch", "Firmware", "DeviceProfile",
    ];

    /// <summary>Gets the independently seeded bounded source-membership cardinalities.</summary>
    internal static readonly int[] OriginCounts = [1, 16, 100, 1_000];

    /// <summary>Gets the shared local/global role used by one-hop uniqueness profiles.</summary>
    internal static readonly Guid ManagerRole = CompositionConsumerModel.Identity(10);

    private static readonly string[] s_metadataChoices = ["yes", "no"];

    /// <summary>Seeds bounded consumer payload beside shared read-only structural views.</summary>
    internal static async Task<CompositionConsumerDataset> CreateAsync(
        TestDatabase database,
        string engine,
        int nodes,
        bool deep,
        CancellationToken cancellationToken
    )
    {
        await using var source = database.CreateContext();
        var connection = source.Database.GetConnectionString()!;
        await CompositionConsumerModel.CreateViewsAsync(source, true, cancellationToken: cancellationToken);
        await CompositionConsumerModel.CreateViewsAsync(source, false, cancellationToken: cancellationToken);
        await using var signals = new CompositionCapacitySignalLabContext(
            ModelCompatibilityDatabase.Options<CompositionCapacitySignalLabContext>(engine, connection));

        await using var groups = new CompositionCapacityGroupContext(
            ModelCompatibilityDatabase.Options<CompositionCapacityGroupContext>(engine, connection));

        FunctionalTestContextPolicy.Configure(signals);
        FunctionalTestContextPolicy.Configure(groups);

        await CompositionConsumerModel.CreatePayloadAsync(signals, true, cancellationToken: cancellationToken);
        await CompositionConsumerModel.CreatePayloadAsync(groups, false, cancellationToken: cancellationToken);

        if (engine == "SqlServer")
        {
            // WHY: Snapshot is an explicit prerequisite of the real sample orchestration, not a silent runtime ALTER.
            await source.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON",
                cancellationToken);
        }

        // WHY: The shared deep profile already varies 1/10/100 generic facts. Two actual consumer facts
        // expose successful/rejected nearest owners without multiplying six sources/grains by 100,000 edges.
        var facts = deep
            ? [nodes - 1, nodes]
            : Enumerable
                .Range(Math.Max(3, nodes - 98), Math.Min(nodes - 2, 99))
                .Prepend(2)
                .Distinct()
                .Order()
                .ToArray();

        var data = new CompositionConsumerDataset(nodes, deep, facts);
        await data.SeedSignalsAsync(signals, cancellationToken: cancellationToken);
        await data.SeedGroupsAsync(groups, cancellationToken: cancellationToken);
        await RefreshStatisticsAsync(signals, engine, cancellationToken: cancellationToken);
        await RefreshStatisticsAsync(groups, engine, cancellationToken: cancellationToken);

        return data;
    }

    /// <summary>Prepares payload estimates outside measurement, as the generic fixture does.</summary>
    private static async Task RefreshStatisticsAsync(
        DbContext context,
        string engine,
        CancellationToken cancellationToken
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();

        foreach (var table in context.Model.GetRelationalModel()
            .Tables)
        {
            if (table.Name is CompositionConsumerModel.SignalNodes
                or CompositionConsumerModel.SignalPeriods
                or CompositionConsumerModel.GroupNodes)
            {
                continue;
            }

            // WHY: A freshly seeded consumer schema otherwise lets asynchronous automatic statistics decide
            // the plan independently of the structural fixture's already explicit statistics preparation.
            var name = sql.DelimitIdentifier(table.Name, table.Schema);
            var statement = engine switch
            {
                "SqlServer" => "UPDATE STATISTICS " + name + " WITH FULLSCAN",
                "MySql" or "MariaDb" => "ANALYZE TABLE " + name,
                _ => "ANALYZE " + name,
            };

            await context.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }

    /// <summary>Returns one independently selected subtree report with genuine source and owner variation.</summary>
    internal SignalApp.SignalReportRequest Report(
        int anchors,
        int metrics,
        int sources,
        int days,
        bool nearest
    )
        => new(
            CompositionConsumerModel.Tenant,
            CompositionConsumerModel.Tree,
            Enumerable
                .Range(Nodes - anchors + 2, anchors - 1)
                .Prepend(1)
                .Select(CompositionConsumerModel.Identity)
                .ToArray(),
            Enumerable
                .Range(101, metrics)
                .Select(CompositionConsumerModel.Identity)
                .ToArray(),
            Enumerable
                .Range(0, sources)
                .Select(source => (SignalApp.CapturePort)source)
                .ToArray(),
            CompositionConsumerModel.FirstDay,
            CompositionConsumerModel.FirstDay.AddDays(days),
            SignalApp.MeasurementGranularity.Daily,
            SignalFields
                .Take(
                    anchors == 1
                        ? 1
                        : 4)
                .Select(field => new SignalApp.TagCriterion(
                    CompositionConsumerModel.Identity(1),
                    field,
                    "yes",
                    nearest
                        ? SignalApp.TagOwnerMode.Nearest
                        : SignalApp.TagOwnerMode.Any))
                .ToArray());

    /// <summary>Returns explicit actual sample routing rules for one bounded origin population.</summary>
    internal static GroupApp.SupervisorRequest Supervisors(
        int origins,
        int fields
    ) => new()
    {
        SourceUserId = SourceUser(origins),
        SearchStart = GroupApp.SearchStart.ParentsOnly,
        Roles = new GroupApp.EitherRole(ManagerRole),
        Groups = new GroupApp.AllEligibleGroups(1),
        Matches = GroupFields
            .Take(fields)
            .Select(field => new GroupApp.MetadataMatch(
                GroupApp.MetadataOwner.SourceUser,
                field,
                GroupApp.MetadataOwner.CandidateMembership,
                field))
            .ToArray(),
    };

    /// <summary>Derives the known parent plan without inspecting the stored nested-set boundaries.</summary>
    internal static int? Parent(
        int key,
        bool balanced,
        bool deep
    )
    {
        if (key == 1)
        {
            return null;
        }

        return deep
            ? key - 1
            : balanced && (key - 2) % 1_000 != 0
                ? 2 + (key - 2) / 1_000 * 1_000
                : 1;
    }

    /// <summary>Checks ancestry from the independent fixture adjacency without inspecting intervals.</summary>
    internal static bool Contains(
        int ancestor,
        int node,
        bool balanced,
        bool deep
    )
    {
        for (int? current = node; current is { } key; current = Parent(key, balanced, deep))
        {
            if (key == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gives each cardinality a distinct source without duplicating hierarchy rows.</summary>
    internal static int SourceUser(
        int origins
    ) => 100_000 + origins;
}
