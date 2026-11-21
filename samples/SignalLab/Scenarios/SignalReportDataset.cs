namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>
/// Seeds small, independent reporting scenarios through the same public import API used by consumers.
/// </summary>
/// <param name="TenantId">The isolated synthetic laboratory.</param>
/// <param name="TreeId">The selected station tree.</param>
/// <param name="Anchors">The independently selected instrument anchors.</param>
/// <param name="Nearest">Whether the fixture contains repeated module and sensor kinds on a path.</param>
internal sealed partial record SignalReportDataset(
    Guid TenantId,
    Guid TreeId,
    Guid[] Anchors,
    bool Nearest
)
{
    /// <summary>Counts the three five-node trees included before a benchmark extends the main hierarchy.</summary>
    internal const int SeedNodeCount = 15;

    /// <summary>Identifies a station kind without tying it to structural depth.</summary>
    internal static Guid StationKind { get; } = Id(101);

    /// <summary>Identifies an instrument-array kind.</summary>
    internal static Guid ArrayKind { get; } = Id(102);

    /// <summary>Identifies a module kind that may repeat on a path.</summary>
    internal static Guid ModuleKind { get; } = Id(103);

    /// <summary>Identifies the authoritative daily measurement owner.</summary>
    internal static Guid SensorKind { get; } = Id(104);

    /// <summary>Identifies the primary additive channel.</summary>
    internal static Guid PulseChannel { get; } = Id(901);

    /// <summary>Identifies a second additive channel in the same unit.</summary>
    internal static Guid SweepChannel { get; } = Id(902);

    /// <summary>Gets the first measurement day in the deterministic report.</summary>
    internal static DateOnly FirstDay { get; } = new(2042, 3, 10);

    /// <summary>Creates a deterministic scenario using its default isolated synthetic tenant.</summary>
    internal static Task<SignalReportDataset> CreateAsync(
        SignalLabContext context,
        int scenario,
        bool nearest,
        CancellationToken cancellationToken
    ) => CreateAsync(context, scenario, nearest, null, cancellationToken);

    /// <summary>Creates either the multilevel temporal report or the repeated-type nearest-owner example.</summary>
    internal static async Task<SignalReportDataset> CreateAsync(
        SignalLabContext context,
        int scenario,
        bool nearest,
        Guid? tenantId,
        CancellationToken cancellationToken
    )
    {
        var tenant = tenantId ?? Id(1000 + scenario);
        var tree = Id(2000 + scenario);
        var scenarioName = scenario switch
        {
            5 => "report",
            6 => "temporal",
            7 => "metadata",
            _ => "nearest",
        };

        await SeedCatalogAsync(context, tenant, cancellationToken);
        var hierarchy = context
            .NestedSet<SignalNode>()
            .ForScope(tenant);

        var branch = CreateBranch(tenant, nearest, scenarioName);

        await hierarchy.InsertForestAsync([new NestedSetTreeImport<SignalNode, Guid>(tree, branch)], cancellationToken);
        AddLinkWindows(context, tenant, nearest);

        AddMetadata(context, tenant, nearest);

        AddScenarioMeasurements(context, tenant, nearest);

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        await SeedIsolationAsync(context, tenant, tree, scenario, cancellationToken);

        return new SignalReportDataset(
            tenant,
            tree,
            nearest
                ? [Id(1), Id(6)]
                : [Id(1), Id(2), Id(3), Id(4)],
            nearest);
    }

    /// <summary>
    /// Builds a valid fixed report selection; callers can explicitly select other modes and metadata values.
    /// </summary>
    internal SignalReportRequest Request(
        TagOwnerMode mode = TagOwnerMode.Any,
        string moduleClass = "Receiver",
        bool includeSensor = true
    )
    {
        var criteria = new List<TagCriterion>
        {
            new(ModuleKind, "ModuleClass", moduleClass, mode),
        };

        if (!Nearest)
        {
            criteria.Add(new TagCriterion(ArrayKind, "Band", "Narrow"));
        }

        if (includeSensor)
        {
            criteria.Add(new TagCriterion(SensorKind, "Cooling", "Liquid", mode));
            criteria.Add(new TagCriterion(SensorKind, "RackCode", "R7", mode));
        }

        return new SignalReportRequest(
            TenantId,
            TreeId,
            Anchors,
            [PulseChannel, SweepChannel],
            [CapturePort.Pulse, CapturePort.Sweep, CapturePort.Burst],
            FirstDay,
            FirstDay.AddDays(2),
            MeasurementGranularity.Daily,
            criteria);
    }

    /// <summary>Labels the fixed demonstration anchors without loading entities into the report.</summary>
    internal static string AnchorName(
        Guid id
    )
    {
        if (id == Id(1))
        {
            return "Station";
        }

        if (id == Id(2))
        {
            return "Narrow-band array";
        }

        if (id == Id(3))
        {
            return "Receiver module";
        }

        if (id == Id(4))
        {
            return "Sensor A";
        }

        return id == Id(5)
            ? "Sensor B"
            : "Sensor P";
    }

    /// <summary>Provides repeatable identities without using GUID order as a hierarchy rule.</summary>
    internal static Guid Id(
        int ordinal
    ) => new(
        ordinal,
        0x51ab,
        0x4000,
        0x80,
        0,
        0,
        0,
        0,
        0,
        0,
        1);

}
