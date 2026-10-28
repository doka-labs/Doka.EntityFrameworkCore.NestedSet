using GroupApp = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;
using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Holds only bounded consumer payload identities, never the persisted structural population.</summary>
/// <param name="Nodes">The existing structural population, retained without a CLR entity buffer.</param>
/// <param name="Deep">Whether sparse payload was chosen for the shared hundred-thousand-depth profile.</param>
/// <param name="Facts">The bounded, deliberately selected measurement owners.</param>
internal sealed record CompositionConsumerDataset(int Nodes, bool Deep, int[] Facts)
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
        TestDatabase database, string engine, int nodes, bool deep)
    {
        await using var source = database.CreateContext();
        var connection = source.Database.GetConnectionString()!;
        await CompositionConsumerModel.CreateViewsAsync(source, true);
        await CompositionConsumerModel.CreateViewsAsync(source, false);
        await using var signals = new CompositionCapacitySignalLabContext(
            ModelCompatibilityDatabase.Options<CompositionCapacitySignalLabContext>(engine, connection));

        await using var groups = new CompositionCapacityGroupContext(
            ModelCompatibilityDatabase.Options<CompositionCapacityGroupContext>(engine, connection));

        await CompositionConsumerModel.CreatePayloadAsync(signals, true);
        await CompositionConsumerModel.CreatePayloadAsync(groups, false);

        if (engine == "SqlServer")
        {
            // WHY: Snapshot is an explicit prerequisite of the real sample orchestration, not a silent runtime ALTER.
            await source.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON", CancellationToken.None);
        }

        // WHY: The shared deep profile already varies 1/10/100 generic facts. Two actual consumer facts
        // expose successful/rejected nearest owners without multiplying six sources/grains by 100,000 edges.
        var facts = deep ? [nodes - 1, nodes]
            : Enumerable.Range(Math.Max(3, nodes - 98), Math.Min(nodes - 2, 99)).Prepend(2)
                .Distinct().Order().ToArray();

        var data = new CompositionConsumerDataset(nodes, deep, facts);
        await data.SeedSignalsAsync(signals);
        await data.SeedGroupsAsync(groups);
        await RefreshStatisticsAsync(signals, engine);
        await RefreshStatisticsAsync(groups, engine);

        return data;
    }

    /// <summary>Prepares payload estimates outside measurement, as the generic fixture does.</summary>
    private static async Task RefreshStatisticsAsync(DbContext context, string engine)
    {
        var sql = context.GetService<ISqlGenerationHelper>();

        foreach (var table in context.Model.GetRelationalModel().Tables)
        {
            if (table.Name is CompositionConsumerModel.SignalNodes or CompositionConsumerModel.SignalPeriods
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

            await context.Database.ExecuteSqlRawAsync(statement, CancellationToken.None);
        }
    }

    /// <summary>Returns one independently selected subtree report with genuine source and owner variation.</summary>
    internal SignalApp.SignalReportRequest Report(int anchors, int metrics, int sources, int days, bool nearest)
        => new(CompositionConsumerModel.Tenant, CompositionConsumerModel.Tree,
            Enumerable.Range(Nodes - anchors + 2, anchors - 1).Prepend(1)
                .Select(CompositionConsumerModel.Identity).ToArray(),
            Enumerable.Range(101, metrics).Select(CompositionConsumerModel.Identity).ToArray(),
            Enumerable.Range(0, sources).Select(source => (SignalApp.CapturePort)source).ToArray(),
            CompositionConsumerModel.FirstDay, CompositionConsumerModel.FirstDay.AddDays(days),
            SignalApp.MeasurementGranularity.Daily,
            SignalFields.Take(anchors == 1 ? 1 : 4).Select(field => new SignalApp.TagCriterion(
                CompositionConsumerModel.Identity(1), field, "yes",
                nearest ? SignalApp.TagOwnerMode.Nearest : SignalApp.TagOwnerMode.Any)).ToArray());

    /// <summary>Returns explicit actual sample routing rules for one bounded origin population.</summary>
    internal static GroupApp.SupervisorRequest Supervisors(int origins, int fields) => new()
    {
        SourceUserId = SourceUser(origins),
        SearchStart = GroupApp.SearchStart.ParentsOnly,
        Roles = new GroupApp.EitherRole(ManagerRole),
        Groups = new GroupApp.AllEligibleGroups(1),
        Matches = GroupFields.Take(fields).Select(field => new GroupApp.MetadataMatch(
            GroupApp.MetadataOwner.SourceUser, field,
            GroupApp.MetadataOwner.CandidateMembership, field)).ToArray(),
    };

    /// <summary>Derives the known parent plan without inspecting the stored nested-set boundaries.</summary>
    internal static int? Parent(int key, bool balanced, bool deep)
    {
        if (key == 1)
        {
            return null;
        }

        return deep ? key - 1
            : balanced && (key - 2) % 1_000 != 0 ? 2 + (((key - 2) / 1_000) * 1_000) : 1;
    }

    /// <summary>Checks ancestry from the independent fixture adjacency without inspecting intervals.</summary>
    internal static bool Contains(int ancestor, int node, bool balanced, bool deep)
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
    internal static int SourceUser(int origins) => 100_000 + origins;

    /// <summary>Creates the sample's real typed catalogs and at most 1,200 physical fact rows.</summary>
    private async Task SeedSignalsAsync(CompositionCapacitySignalLabContext context)
    {
        var tenant = CompositionConsumerModel.Tenant;
        var type = CompositionConsumerModel.Identity(1);
        context.NodeKinds.Add(new SignalApp.NodeKind { TenantId = tenant, Id = type, Code = "Capacity" });

        foreach (var field in SignalFields)
        {
            context.TagDefinitions.Add(new SignalApp.TagDefinition
            {
                TenantId = tenant, TypeId = type, FieldCode = field, Label = field,
            });
        }

        foreach (var field in SignalFields)
        {
            foreach (var value in s_metadataChoices)
            {
                context.TagChoices.Add(new SignalApp.TagChoice
                {
                    TenantId = tenant, TypeId = type, FieldCode = field, Value = value,
                });
            }

            foreach (var key in Facts.Prepend(1))
            {
                context.TagValues.Add(new SignalApp.TagValue
                {
                    TenantId = tenant, NodeId = CompositionConsumerModel.Identity(key), TypeId = type,
                    FieldCode = field, Value = field == "ModuleClass" && key != 1 && key % 2 != 0 ? "no" : "yes",
                });
            }
        }

        for (var metric = 101; metric <= 102; metric++)
        {
            var metricId = CompositionConsumerModel.Identity(metric);
            context.SignalChannels.Add(new SignalApp.SignalChannel
            {
                TenantId = tenant, Id = metricId, MeasurementTypeId = type,
                Name = "Capacity metric", UnitCode = "unit",
            });

            foreach (var key in Facts)
            {
                var node = CompositionConsumerModel.Identity(key);

                for (var day = 0; day < 2; day++)
                {
                    var date = CompositionConsumerModel.FirstDay.AddDays(day);
                    var value = (metric - 100) * 10 + day + key % 7;
                    context.PulseRecords.Add(new SignalApp.PulseRecord
                    {
                        TenantId = tenant, ChannelId = metricId, NodeId = node,
                        Granularity = SignalApp.MeasurementGranularity.Daily, Day = date, PulseCount = value,
                    });
                    context.SweepRecords.Add(new SignalApp.SweepRecord
                    {
                        TenantId = tenant, StreamId = metricId, ReceiverId = node,
                        Resolution = SignalApp.MeasurementGranularity.Daily, CompletedOn = date, IntegratedTicks = value * 2,
                    });
                    context.BurstRecords.Add(new SignalApp.BurstRecord
                    {
                        TenantId = tenant, SignalId = metricId, DeviceId = node,
                        BucketSize = SignalApp.MeasurementGranularity.Daily, CapturedOn = date, Magnitude = value * 3,
                    });
                }
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }

    /// <summary>Seeds four source populations, one roleholder, and sixteen real scalar routing fields.</summary>
    private async Task SeedGroupsAsync(CompositionCapacityGroupContext context)
    {
        var tenant = CompositionConsumerModel.Tenant;
        context.GroupTypes.Add(new GroupApp.GroupType { TenantId = tenant, Id = 1, Name = "Capacity" });
        context.Roles.Add(new GroupApp.Role { TenantId = tenant, Id = ManagerRole, Name = "Manager" });

        for (var hop = 0; hop < 8; hop++)
        {
            context.Roles.Add(new GroupApp.Role
            {
                TenantId = tenant, Id = CompositionConsumerModel.Identity(201 + hop), Name = "Chain manager",
            });
        }

        foreach (var key in OriginCounts.Select(SourceUser).Prepend(2).Concat(Enumerable.Range(200, 8)))
        {
            var user = new GroupApp.User { Id = key, TenantId = tenant, Name = "Capacity user", Position = "User" };
            context.Users.Add(user);
            context.Entry(user).Property<Guid>(GroupApp.UserHierarchyProperties.TreeId).CurrentValue
                = CompositionConsumerModel.Identity(key + 1_000_000);
            context.Entry(user).Property<long>(GroupApp.UserHierarchyProperties.Left).CurrentValue = 1;
            context.Entry(user).Property<long>(GroupApp.UserHierarchyProperties.Right).CurrentValue = 2;
        }

        // WHY: These few independent user roots are unrelated to the measured group hierarchy. Fixture seeding
        // supplies canonical coordinates once; the consumer's actual query/save code remains unchanged.
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);

        context.UserGroupMemberships.Add(new GroupApp.UserGroupMembership
        {
            TenantId = tenant, UserId = 2, UserGroupId = 1, LeadershipPriority = 1,
        });
        context.MembershipRoles.Add(new GroupApp.MembershipRole
        {
            TenantId = tenant, UserId = 2, UserGroupId = 1, RoleId = ManagerRole,
        });
        context.UserRoles.Add(new GroupApp.UserRole { TenantId = tenant, UserId = 2, RoleId = ManagerRole });

        for (var hop = 0; hop < 8; hop++)
        {
            var group = Nodes - hop - 1;
            context.UserGroupMemberships.AddRange(
                new GroupApp.UserGroupMembership
                {
                    TenantId = tenant, UserId = 200 + hop, UserGroupId = group, LeadershipPriority = 1,
                },
                new GroupApp.UserGroupMembership
                {
                    TenantId = tenant, UserId = 200 + hop, UserGroupId = Nodes, LeadershipPriority = 1,
                });
            context.MembershipRoles.Add(new GroupApp.MembershipRole
            {
                TenantId = tenant, UserId = 200 + hop, UserGroupId = group,
                RoleId = CompositionConsumerModel.Identity(201 + hop),
            });
        }

        foreach (var count in OriginCounts)
        {
            for (var offset = 0; offset < count; offset++)
            {
                context.UserGroupMemberships.Add(new GroupApp.UserGroupMembership
                {
                    TenantId = tenant, UserId = SourceUser(count), UserGroupId = Nodes - offset,
                });
            }
        }

        foreach (var field in GroupFields)
        {
            context.UserMetadataDefinitions.Add(new GroupApp.UserMetadataDefinition
            {
                TenantId = tenant, FieldCode = field,
            });
            context.MembershipMetadataDefinitions.Add(new GroupApp.MembershipMetadataDefinition
            {
                TenantId = tenant, GroupTypeId = 1, FieldCode = field,
            });
            context.MembershipMetadatas.Add(new GroupApp.MembershipMetadata
            {
                TenantId = tenant, GroupTypeId = 1, UserId = 2, UserGroupId = 1,
                FieldCode = field, Value = "lane-blue",
            });

            foreach (var count in OriginCounts)
            {
                context.UserMetadatas.Add(new GroupApp.UserMetadata
                {
                    TenantId = tenant, UserId = SourceUser(count), FieldCode = field, Value = "lane-blue",
                });
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }
}
