namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>
/// Seeds small, independent reporting scenarios through the same public import API used by consumers.
/// </summary>
/// <param name="TenantId">The isolated synthetic laboratory.</param>
/// <param name="TreeId">The selected station tree.</param>
/// <param name="Anchors">The independently selected instrument anchors.</param>
/// <param name="Nearest">Whether the fixture contains repeated module and sensor kinds on a path.</param>
internal sealed record SignalReportDataset(
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

    /// <summary>Creates either the multilevel temporal report or the repeated-type nearest-owner example.</summary>
    internal static async Task<SignalReportDataset> CreateAsync(
        SignalLabContext context,
        int scenario,
        bool nearest,
        CancellationToken cancellationToken,
        Guid? tenantId = null
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

        await hierarchy.InsertForestAsync([new NestedSetTreeImport<SignalNode, Guid>(tree, branch)], cancellationToken);
        var edges = nearest? new[] { (1, 3), (3, 4), (4, 5), (5, 6) } : [(1, 2), (2, 3), (3, 4), (3, 5)];

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
                    ToDate = !nearest && child == 5 ? FirstDay : null,
                });
        }

        AddMetadata(context, tenant, nearest);

        if (nearest)
        {
            AddMeasurements(context, tenant, Id(6), FirstDay, 10);
        }
        else
        {
            AddMeasurements(context, tenant, Id(4), FirstDay, 10);
            AddMeasurements(context, tenant, Id(4), FirstDay.AddDays(1), 20);
            AddMeasurements(context, tenant, Id(5), FirstDay, 40);
            AddMeasurements(context, tenant, Id(5), FirstDay.AddDays(1), 50);
            // WHY: Stored intermediate and monthly aggregates deliberately exist; the report's type and grain exclude
            // them.
            context.PulseRecords.Add(
                new PulseRecord
                {
                    TenantId = tenant,
                    ChannelId = PulseChannel,
                    NodeId = Id(3),
                    Day = FirstDay,
                    Granularity = MeasurementGranularity.Daily,
                    PulseCount = 1000,
                });
            context.PulseRecords.Add(
                new PulseRecord
                {
                    TenantId = tenant,
                    ChannelId = PulseChannel,
                    NodeId = Id(4),
                    Day = FirstDay,
                    Granularity = MeasurementGranularity.Monthly,
                    PulseCount = 9000,
                });
        }

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        await SeedIsolationAsync(context, tenant, tree, scenario, cancellationToken);

        return new SignalReportDataset(tenant, tree, nearest ? [Id(1), Id(6)] : [Id(1), Id(2), Id(3), Id(4)], nearest);
    }

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

        foreach (var (parent, child) in new[] { (10, 11), (11, 12), (12, 13), (12, 14) })
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
        foreach (var (parent, child) in new[] { (1, 2), (2, 3), (3, 4), (3, 5) })
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

        return id == Id(5) ? "Sensor B" : "Sensor P";
    }

    /// <summary>Provides repeatable identities without using GUID order as a hierarchy rule.</summary>
    internal static Guid Id(
        int ordinal
    ) => new(ordinal, 0x51ab, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1);

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

    /// <summary>Persists the fixed, case-sensitive field catalog and two compatible additive channels.</summary>
    private static async Task SeedCatalogAsync(
        SignalLabContext context,
        Guid tenant,
        CancellationToken cancellationToken
    )
    {
        context.NodeKinds.AddRange(
            new NodeKind
            {
                TenantId = tenant,
                Id = StationKind,
                Code = "Station",
            },
            new NodeKind
            {
                TenantId = tenant,
                Id = ArrayKind,
                Code = "Array",
            },
            new NodeKind
            {
                TenantId = tenant,
                Id = ModuleKind,
                Code = "Module",
            },
            new NodeKind
            {
                TenantId = tenant,
                Id = SensorKind,
                Code = "Sensor",
            });

        (Guid Type, string Field, string[] Values)[] fields =
        [
            (ArrayKind, "Band", ["Narrow", "Wide"]),
            (ModuleKind, "ModuleClass", ["Receiver", "Relay"]),
            (SensorKind, "Cooling", ["Liquid", "Air"]),
            (SensorKind, "CaptureMode", ["Continuous", "Triggered"]),
            (SensorKind, "RackCode", ["R7", "R9", "x' OR 1=1 --"]),
        ];

        foreach (var (type, field, values) in fields)
        {
            context.TagDefinitions.Add(
                new TagDefinition
                {
                    TenantId = tenant,
                    TypeId = type,
                    FieldCode = field,
                    Label = field,
                });

            foreach (var value in values)
            {
                context.TagChoices.Add(
                    new TagChoice
                    {
                        TenantId = tenant,
                        TypeId = type,
                        FieldCode = field,
                        Value = value,
                    });
            }
        }

        context.SignalChannels.AddRange(
            new SignalChannel
            {
                TenantId = tenant,
                Id = PulseChannel,
                MeasurementTypeId = SensorKind,
                Name = "Pulse ticks",
                UnitCode = "count",
            },
            new SignalChannel
            {
                TenantId = tenant,
                Id = SweepChannel,
                MeasurementTypeId = SensorKind,
                Name = "Sweep ticks",
                UnitCode = "count",
            });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Stores per-node fields, including a deliberate split-owner mismatch in the nearest example.</summary>
    private static void AddMetadata(
        SignalLabContext context,
        Guid tenant,
        bool nearest
    )
    {
        if (!nearest)
        {
            Add(2, ArrayKind, "Band", "Narrow");
        }

        Add(3, ModuleKind, "ModuleClass", "Receiver");
        Add(nearest ? 5 : 4, SensorKind, "CaptureMode", "Continuous");
        Add(nearest ? 6 : 5, SensorKind, "CaptureMode", "Continuous");

        if (nearest)
        {
            Add(4, ModuleKind, "ModuleClass", "Relay");
            Add(5, SensorKind, "Cooling", "Air");
            Add(5, SensorKind, "RackCode", "R7");
            Add(6, SensorKind, "Cooling", "Liquid");
            Add(6, SensorKind, "RackCode", "R9");
        }
        else
        {
            Add(4, SensorKind, "Cooling", "Liquid");
            Add(4, SensorKind, "RackCode", "R7");
            Add(5, SensorKind, "Cooling", "Liquid");
            Add(5, SensorKind, "RackCode", "R7");
        }

        return;

        void Add(
            int node,
            Guid type,
            string field,
            string value
        ) => context.TagValues.Add(
            new TagValue
            {
                TenantId = tenant,
                NodeId = Id(node),
                TypeId = type,
                FieldCode = field,
                Value = value,
            });
    }

    /// <summary>Adds the same fact keys independently to all sources and two additive channels.</summary>
    private static void AddMeasurements(
        SignalLabContext context,
        Guid tenant,
        Guid node,
        DateOnly day,
        double amount
    )
    {
        foreach (var (channel, multiplier) in new[] { (PulseChannel, 1), (SweepChannel, 2) })
        {
            context.PulseRecords.Add(
                new PulseRecord
                {
                    TenantId = tenant,
                    ChannelId = channel,
                    NodeId = node,
                    Granularity = MeasurementGranularity.Daily,
                    Day = day,
                    PulseCount = amount * multiplier,
                });
            context.SweepRecords.Add(
                new SweepRecord
                {
                    TenantId = tenant,
                    StreamId = channel,
                    ReceiverId = node,
                    Resolution = MeasurementGranularity.Daily,
                    CompletedOn = day,
                    IntegratedTicks = amount * multiplier * 2,
                });
            context.BurstRecords.Add(
                new BurstRecord
                {
                    TenantId = tenant,
                    SignalId = channel,
                    DeviceId = node,
                    BucketSize = MeasurementGranularity.Daily,
                    CapturedOn = day,
                    Magnitude = amount * multiplier * 3,
                });
        }
    }
}
