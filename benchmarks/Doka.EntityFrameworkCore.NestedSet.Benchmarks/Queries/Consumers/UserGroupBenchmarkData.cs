using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Seeds the synthetic sample's group, membership and role fixtures without duplicating its model.</summary>
internal static class UserGroupBenchmarkData
{
    /// <summary>Names the actual sample's finite metadata catalog instead of unsupported field codes.</summary>
    internal static IReadOnlyList<string> Fields { get; } = Array.AsReadOnly(
    [
        "Route",
        "ChannelClass",
        "ManagerRoute",
        "RoutingZone",
        "Lane",
        "Cluster",
        "Protocol",
        "Mode",
        "Profile",
        "Phase",
        "Zone",
        "Port",
        "Rack",
        "Batch",
        "Firmware",
        "DeviceProfile",
    ]);

    /// <summary>Prepares the selected group population and exact source-origin count.</summary>
    internal static async Task<(SupervisorScenarioFixture Data, SupervisorRequest Request)> CreateAsync(
        UserGroupContext context,
        int nodes,
        BenchmarkShape shape,
        int origins,
        int matches
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodes, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(origins, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(origins, nodes);
        ArgumentOutOfRangeException.ThrowIfLessThan(matches, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(matches, Fields.Count);
        var data = await SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var count = nodes - 7;
        var groups = new UserGroup[count];
        var branches = new NestedSetBranch<UserGroup>[count];
        var children = new List<int>?[count];

        for (var index = 0; index < count; index++)
        {
            groups[index] = new UserGroup
            {
                Name = FormattableString.Invariant($"Measured {index:D9}"),
                TypeId = 4,
            };

            if (index == 0)
            {
                continue;
            }

            var parent = shape switch
            {
                BenchmarkShape.Wide => 0,
                BenchmarkShape.Deep => index - 1,
                BenchmarkShape.Balanced => (index - 1) / 2,
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };

            (children[parent] ??= []).Add(index);
        }

        for (var index = count - 1; index >= 0; index--)
        {
            branches[index] = new NestedSetBranch<UserGroup>(
                groups[index],
                children[index]
                    ?.Select(child => branches[child])
                    .ToArray());
        }

        await context
            .NestedSet<UserGroup>()
            .ForScope(data.TenantId)
            .InsertSubtreeAsync(branches[0], data.Groups[0], CancellationToken.None);

        // WHY: Derive added link windows from the synthetic fixture instead of a copied calendar date.
        var observationDay = data.Request().OnDay
            ?? throw new InvalidOperationException("The synthetic supervisor fixture requires an observation day.");

        foreach (var group in groups)
        {
            context.GroupRelationPeriods.Add(
                new GroupRelationPeriod
                {
                    TenantId = data.TenantId,
                    ParentId = group.ParentId!.Value,
                    ChildId = group.Id,
                    FromDate = observationDay,
                });
        }

        var baseOrigins = new HashSet<int>
        {
            data.Groups[5],
            data.Groups[6]
        };

        var extra = origins == 1
            ? []
            : data
                .Groups
                .Concat(groups.Select(group => group.Id))
                .Where(group => !baseOrigins.Contains(group))
                .Take(origins - 2)
                .ToArray();

        foreach (var group in extra)
        {
            context.UserGroupMemberships.Add(
                new UserGroupMembership
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    UserGroupId = group,
                    LeadershipPriority = 50,
                });
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await AddFieldsAsync(context, data, matches);
        var request = data.Request(group: origins == 1 ? data.Groups[5] : null) with
        {
            Matches = Fields
                .Take(matches)
                .Select(field => new MetadataMatch(
                    MetadataOwner.SourceUser,
                    field,
                    MetadataOwner.CandidateMembership,
                    field))
                .ToArray(),
        };

        BenchmarkFixture.Require(
            await context
                .Groups
                .IgnoreQueryFilters()
                .LongCountAsync(CancellationToken.None)
            == nodes,
            "UserGroups benchmark population differs from its Nodes parameter.");

        return (data, request);
    }

    /// <summary>Registers and populates the same finite field list on exact candidate memberships.</summary>
    private static async Task AddFieldsAsync(
        UserGroupContext context,
        SupervisorScenarioFixture data,
        int matches
    )
    {
        var memberships = await context
            .UserGroupMemberships
            .AsNoTracking()
            .Where(member => member.TenantId == data.TenantId && member.UserId != data.Users[0])
            .Join(
                context.Groups,
                member => member.UserGroupId,
                group => group.Id,
                (member, group) => new
                {
                    member.UserId,
                    member.UserGroupId,
                    GroupType = group.TypeId!.Value
                })
            .ToArrayAsync(CancellationToken.None);

        var oldFields = new HashSet<string>(
            ["Route", "ManagerRoute", "ChannelClass", "RoutingZone"],
            StringComparer.Ordinal);

        foreach (var field in Fields
                     .Skip(1)
                     .Take(matches - 1))
        {
            if (!oldFields.Contains(field))
            {
                context.UserMetadataDefinitions.Add(
                    new UserMetadataDefinition
                    {
                        TenantId = data.TenantId,
                        FieldCode = field,
                    });

                foreach (var type in new[] { 1, 2, 3, 4 })
                {
                    context.MembershipMetadataDefinitions.Add(
                        new MembershipMetadataDefinition
                        {
                            TenantId = data.TenantId,
                            GroupTypeId = type,
                            FieldCode = field,
                        });
                }
            }

            context.UserMetadatas.Add(
                new UserMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    FieldCode = field,
                    Value = "measured-match",
                });

            foreach (var member in memberships)
            {
                context.MembershipMetadatas.Add(
                    new MembershipMetadata
                    {
                        TenantId = data.TenantId,
                        UserId = member.UserId,
                        UserGroupId = member.UserGroupId,
                        GroupTypeId = member.GroupType,
                        FieldCode = field,
                        Value = "measured-match",
                    });
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }
}
