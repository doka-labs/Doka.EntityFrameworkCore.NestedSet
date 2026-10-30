namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>
/// Demonstrates explicit team, classification, rank, and chain rules without domain rules in NestedSet.
/// </summary>
internal static class GroupSupervisorScenario
{
    /// <summary>Runs one independently selected upward-query flow using fresh tenant-owned sample data.</summary>
    internal static async Task RunAsync(
        SampleDatabaseConfiguration configuration,
        string scenario,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await using var context = UserGroupContexts.Create(configuration);
        var fixture = await SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), cancellationToken);
        var request = fixture.Request(group: fixture.Groups[5]);
        await SampleConsole.HeadingAsync(scenario.ToUpperInvariant(), cancellationToken);
        await SampleConsole.WriteLineAsync(
            "Group path: G0 -> G1 -> G2 -> G3 -> G4 -> G5; Independent team branches from G1.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "Group types: G0/G1/G2=Manager; G3=Supervisor; G4=Foreman; G5/Independent team=User team.",
            cancellationToken);

        switch (scenario)
        {
            case "team-supervisors":
                foreach (var start in new[] { SearchStart.IncludeSelf, SearchStart.ParentsOnly })
                {
                    foreach (var roles in new SupervisorRoles[]
                             {
                                 new MembershipRoleFilter(fixture.ManagerRoleId),
                                 new GlobalUserRole(fixture.ManagerRoleId),
                                 new EitherRole(fixture.ManagerRoleId),
                             })
                    {
                        var result = await SupervisorQueries.ResolveAsync(
                            context,
                            fixture.TenantId,
                            request with
                            {
                                StartGroupId = null,
                                SearchStart = start,
                                Roles = roles,
                            },
                            SupervisorSelection.All,
                            cancellationToken);

                        await PrintAsync(
                            $"{start}; {roles.GetType().Name}; All",
                            result,
                            fixture,
                            details,
                            cancellationToken);
                    }
                }

                var unique = await SupervisorQueries.ResolveAsync(
                    context,
                    fixture.TenantId,
                    request,
                    SupervisorSelection.RequireUnique,
                    cancellationToken);

                var priority = await SupervisorQueries.ResolveAsync(
                    context,
                    fixture.TenantId,
                    request,
                    SupervisorSelection.HighestBusinessPriority,
                    cancellationToken);

                await PrintAsync(
                    "RequireUnique (Anna and Alex remain ambiguous)",
                    unique,
                    fixture,
                    details,
                    cancellationToken);

                await PrintAsync(
                    "HighestBusinessPriority (Anna=10; Alex=20)",
                    priority,
                    fixture,
                    details,
                    cancellationToken);
                break;
            case "group-level-supervisors":
                await SeedOwnerDemonstrationAsync(context, fixture, cancellationToken);
                foreach (var groups in new SupervisorGroups[]
                         {
                             new NthEligibleGroup([1], 1), new NthTypeGroupThenValidate(1, 1)
                         })
                {
                    var result = await SupervisorQueries.ResolveAsync(
                        context,
                        fixture.TenantId,
                        request with { Groups = groups },
                        SupervisorSelection.RequireUnique,
                        cancellationToken);

                    await PrintAsync(
                        groups.GetType()
                            .Name,
                        result,
                        fixture,
                        details,
                        cancellationToken);
                }

                foreach (var owners in new[]
                         {
                             (MetadataOwner.SourceUser, MetadataOwner.CandidateMembership),
                             (MetadataOwner.SourceMembership, MetadataOwner.CandidateGroup),
                             (MetadataOwner.SourceGroup, MetadataOwner.CandidateUser),
                         })
                {
                    var matched = await SupervisorQueries.ResolveAsync(
                        context,
                        fixture.TenantId,
                        request with
                        {
                            Groups = new NthTypeGroupThenValidate(1, 1),
                            Matches =
                            [
                                request.Matches[0], new(owners.Item1, "ChannelClass", owners.Item2, "ChannelClass"),
                            ],
                        },
                        SupervisorSelection.RequireUnique,
                        cancellationToken);

                    await PrintAsync(
                        $"Route AND ChannelClass: {owners.Item1} -> {owners.Item2}",
                        matched,
                        fixture,
                        details,
                        cancellationToken);
                }

                break;
            case "ancestor-selection":
                await DemonstrateDirectUserAncestorsAsync(context, fixture, cancellationToken);
                var structural = await context
                    .NestedSet<UserGroup>()
                    .ForScope(fixture.TenantId)
                    .SelectAncestors(
                        group => group.Id == fixture.Groups[5],
                        (source, ancestor, distance) => new
                        {
                            ancestor.Name,
                            Distance = distance
                        })
                    .Where(row => row.Distance == 2)
                    .ToArrayAsync(cancellationToken);

                await SampleConsole.WriteLineAsync(
                    "Structural distance 2: " + string.Join(", ", structural.Select(row => row.Name)),
                    cancellationToken);

                var secondGroup = await SupervisorQueries.ResolveAsync(
                    context,
                    fixture.TenantId,
                    request with { Groups = new NthEligibleGroup([], 2) },
                    SupervisorSelection.RequireUnique,
                    cancellationToken);

                var secondPerson = await SupervisorQueries.ResolveAsync(
                    context,
                    fixture.TenantId,
                    request with { Groups = new AllEligibleGroups() },
                    SupervisorSelection.NthPerson(2),
                    cancellationToken);

                await PrintAsync("Second eligible group: Ben", secondGroup, fixture, details, cancellationToken);
                await PrintAsync("Second ranked person: Alex", secondPerson, fixture, details, cancellationToken);
                break;
            case "supervisor-chain":
                await context.UserGroupMemberships.AddAsync(
                    new UserGroupMembership
                    {
                        TenantId = fixture.TenantId,
                        UserId = fixture.Users[1],
                        UserGroupId = fixture.Groups[6],
                        LeadershipPriority = 15,
                    },
                    cancellationToken);

                await context.SaveChangesAsync(cancellationToken);
                var step = new SupervisorStep(
                    SearchStart.ParentsOnly,
                    request.Roles,
                    request.Groups,
                    request.Matches,
                    request.OnDay,
                    SupervisorSelection.HighestBusinessPriority);

                foreach (var routing in new[] { RoutingOrigin.InitialSubject, RoutingOrigin.CurrentSubject })
                {
                    var chainRequest = new SupervisorChainRequest
                    {
                        SourceUserId = fixture.Users[0],
                        StartGroupId = fixture.Groups[5],
                        Steps = [step, step],
                        RoutingOrigin = routing,
                        MaxFrontierStates = 32,
                        MaxVisitedStates = 128,
                        MaxResultRows = 256,
                    };

                    var original = await SupervisorQueries.FollowOriginalPathAsync(
                        context,
                        fixture.TenantId,
                        chainRequest,
                        cancellationToken);

                    var expanded = await SupervisorQueries.FollowAllMembershipsAsync(
                        context,
                        fixture.TenantId,
                        chainRequest,
                        cancellationToken);

                    await PrintChainAsync($"OriginalPath; {routing}", original, fixture, details, cancellationToken);
                    await PrintChainAsync($"AllMemberships; {routing}", expanded, fixture, details, cancellationToken);
                }

                await DemonstrateChainBoundariesAsync(context, fixture, step, details, cancellationToken);
                break;
            default:
                throw new ArgumentException("Unknown upward-query scenario.", nameof(scenario));
        }
    }

    /// <summary>Shows all three explicit source-owner choices and same-person AND using real typed metadata.</summary>
    private static async Task SeedOwnerDemonstrationAsync(
        UserGroupContext context,
        SupervisorScenarioFixture data,
        CancellationToken cancellationToken
    )
    {
        await context.AddRangeAsync(
            [
                new UserMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    FieldCode = "ChannelClass",
                    Value = "channel-42",
                },
                new MembershipMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    UserGroupId = data.Groups[5],
                    GroupTypeId = 4,
                    FieldCode = "ChannelClass",
                    Value = "channel-42",
                },
                new GroupMetadata
                {
                    TenantId = data.TenantId,
                    UserGroupId = data.Groups[5],
                    GroupTypeId = 4,
                    FieldCode = "ChannelClass",
                    Value = "channel-42",
                },
                new UserMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[3],
                    FieldCode = "ChannelClass",
                    Value = "channel-42",
                },
                new MembershipMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[3],
                    UserGroupId = data.Groups[2],
                    GroupTypeId = 1,
                    FieldCode = "ChannelClass",
                    Value = "channel-42",
                },
                new GroupMetadata
                {
                    TenantId = data.TenantId,
                    UserGroupId = data.Groups[2],
                    GroupTypeId = 1,
                    FieldCode = "ChannelClass",
                    Value = "channel-42",
                },
            ],
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Keeps direct user ancestors independent of the group-derived supervisor relation.</summary>
    private static async Task DemonstrateDirectUserAncestorsAsync(
        UserGroupContext context,
        SupervisorScenarioFixture data,
        CancellationToken cancellationToken
    )
    {
        var users = context
            .NestedSet<User>()
            .ForScope(data.TenantId);

        await users.MoveToAsync(data.Users[0], data.Users[1], cancellationToken);
        await users.MoveToAsync(data.Users[1], data.Users[3], cancellationToken);
        await context.UserRoles.AddRangeAsync(
            [
                new UserRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    RoleId = data.ManagerRoleId,
                },
                new UserRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[3],
                    RoleId = data.ManagerRoleId,
                },
            ],
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        var ancestors = users.SelectAncestors(
            user => user.Id == data.Users[0],
            (source, ancestor, distance) => new
            {
                ancestor.Id,
                ancestor.Name,
                Distance = distance,
            });

        var exactSecond = await ancestors
            .Where(row => row.Distance == 2)
            .ToArrayAsync(cancellationToken);

        var secondRoleholder = await ancestors
            .Where(row => context.UserRoles.Any(role =>
                role.TenantId == data.TenantId && role.UserId == row.Id && role.RoleId == data.ManagerRoleId))
            .OrderBy(row => row.Distance)
            .Skip(1)
            .Take(1)
            .ToArrayAsync(cancellationToken);

        await SampleConsole.WriteLineAsync(
            "Direct User hierarchy: structural ancestor 2 = "
            + string.Join(", ", exactSecond.Select(row => row.Name))
            + "; matching roleholder 2 = "
            + string.Join(", ", secondRoleholder.Select(row => row.Name)),
            cancellationToken);
    }

    /// <summary>
    /// Demonstrates branching, a detected person cycle, and an explicitly incomplete rejected budget.
    /// </summary>
    private static async Task DemonstrateChainBoundariesAsync(
        UserGroupContext context,
        SupervisorScenarioFixture data,
        SupervisorStep step,
        bool details,
        CancellationToken cancellationToken
    )
    {
        var request = new SupervisorChainRequest
        {
            SourceUserId = data.Users[0],
            StartGroupId = data.Groups[5],
            Steps = [step with { Selection = SupervisorSelection.All }, step],
            RoutingOrigin = RoutingOrigin.InitialSubject,
            MaxFrontierStates = 32,
            MaxVisitedStates = 128,
            MaxResultRows = 256,
        };

        var branching = await SupervisorQueries.FollowOriginalPathAsync(
            context,
            data.TenantId,
            request,
            cancellationToken);

        await PrintChainAsync(
            "All branches keep their own origin and visited-person path",
            branching,
            data,
            details,
            cancellationToken);

        await context.UserGroupMemberships.AddAsync(
            new UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[0],
                UserGroupId = data.Groups[0],
                LeadershipPriority = 10,
            },
            cancellationToken);

        await context.MembershipRoles.AddAsync(
            new MembershipRole
            {
                TenantId = data.TenantId,
                UserId = data.Users[0],
                UserGroupId = data.Groups[0],
                RoleId = data.ManagerRoleId,
            },
            cancellationToken);

        await context.UserMetadatas.AddAsync(
            new UserMetadata
            {
                TenantId = data.TenantId,
                UserId = data.Users[0],
                FieldCode = "ManagerRoute",
                Value = "only-initial-person",
            },
            cancellationToken);

        await context.MembershipMetadatas.AddAsync(
            new MembershipMetadata
            {
                TenantId = data.TenantId,
                UserId = data.Users[0],
                UserGroupId = data.Groups[0],
                GroupTypeId = 1,
                FieldCode = "ManagerRoute",
                Value = "only-initial-person",
            },
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        var cycle = await SupervisorQueries.FollowOriginalPathAsync(
            context,
            data.TenantId,
            request with
            {
                Steps =
                [
                    step,
                    step with
                    {
                        Groups = new NthTypeGroupThenValidate(1, 3),
                        Matches =
                        [
                            new MetadataMatch(
                                MetadataOwner.SourceUser,
                                "ManagerRoute",
                                MetadataOwner.CandidateMembership,
                                "ManagerRoute"),
                        ],
                    },
                ],
            },
            cancellationToken);

        await PrintChainAsync(
            "Repeated initial person is rejected before becoming another successful hop",
            cycle,
            data,
            details,
            cancellationToken);

        var limited = await SupervisorQueries.FollowOriginalPathAsync(
            context,
            data.TenantId,
            request with { MaxResultRows = 1 },
            cancellationToken);

        SampleConsole.Require(
            !limited.Complete && limited.Branches.Any(branch => branch.Status == SupervisorChainStatus.LimitExceeded),
            "The negative budget demonstration unexpectedly claimed a complete result.");

        // WHY: Deliberate rejection is checked like the original rejected scenario;
        // normal output rejects incomplete results.
        await SampleConsole.WriteLineAsync(
            "Expected rejected request: LimitExceeded; incomplete, no supervisor result may be consumed.",
            cancellationToken);
    }

    /// <summary>Prints person evidence with explicit origins rather than hiding ambiguity behind First.</summary>
    private static async Task PrintAsync(
        string title,
        SupervisorResolutionBatch batch,
        SupervisorScenarioFixture data,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.WriteLineAsync($"{title}: {batch.Status}", cancellationToken);
        foreach (var origin in batch.Origins.OrderBy(row => row.StartGroupId))
        {
            var originLabel = origin.StartGroupId is { } group
                ? SupervisorScenarioData.GroupLabel(data, group)
                : "No membership";

            await SampleConsole.WriteLineAsync(
                $"  Origin {originLabel}"
                + (details ? $" (group {origin.StartGroupId})" : string.Empty)
                + $": {origin.Status}",
                cancellationToken);

            foreach (var row in origin
                         .Evidence
                         .OrderBy(row => row.Distance)
                         .ThenBy(row => row.CandidateUserId))
            {
                await SampleConsole.WriteLineAsync(
                    $"    {SupervisorScenarioData.PersonLabel(data, row.CandidateUserId)}"
                    + $"/{SupervisorScenarioData.GroupLabel(data, row.FoundGroupId)}, distance {row.Distance}, "
                    + $"priority {row.LeadershipPriority}"
                    + (details
                        ? $", user {row.CandidateUserId}, group {row.FoundGroupId}, "
                        + $"tenant {row.TenantId}, tree {row.StartTreeId}"
                        : string.Empty),
                    cancellationToken);
            }
        }
    }

    /// <summary>Prints every successful hop and explicit branch ending; incomplete limits are failures.</summary>
    private static async Task PrintChainAsync(
        string title,
        SupervisorChainResult result,
        SupervisorScenarioFixture data,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.WriteLineAsync($"{title}: {result.Status}", cancellationToken);
        foreach (var branch in result.Branches)
        {
            await SampleConsole.WriteLineAsync(
                $"  Origin {SupervisorScenarioData.GroupLabel(data, branch.InitialGroupId)}: "
                + string.Join(
                    " -> ",
                    branch.Hops.Select(hop =>
                        SupervisorScenarioData.PersonLabel(data, hop.Evidence.CandidateUserId)
                        + "/"
                        + SupervisorScenarioData.GroupLabel(data, hop.Evidence.FoundGroupId)))
                + $"; {branch.Status}",
                cancellationToken);

            if (details)
            {
                foreach (var hop in branch.Hops)
                {
                    await SampleConsole.WriteLineAsync(
                        $"    Hop {hop.Number}: user {hop.Evidence.CandidateUserId}, "
                        + $"group {hop.Evidence.FoundGroupId}, source {hop.Evidence.SourceUserId}, "
                        + $"start {hop.Evidence.StartGroupId}, tenant {hop.Evidence.TenantId}, "
                        + $"tree {hop.Evidence.StartTreeId}, original user {hop.InitialUserId}, "
                        + $"original group {hop.InitialGroupId}, original tree {hop.InitialTreeId}",
                        cancellationToken);
                }
            }
        }

        SampleConsole.Require(result.Complete, "The explicit chain budget was exceeded; this is an incomplete result.");
    }
}
