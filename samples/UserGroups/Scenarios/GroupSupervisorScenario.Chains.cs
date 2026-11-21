namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class GroupSupervisorScenario
{
    /// <summary>Runs the independently selected supervisor-chain demonstration.</summary>
    private static async Task RunChainAsync(
        UserGroupContext context,
        SupervisorScenarioFixture fixture,
        SupervisorRequest request,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
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

        RoutingOrigin[] routingOrigins = [RoutingOrigin.InitialSubject, RoutingOrigin.CurrentSubject];

        foreach (var routing in routingOrigins)
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

            await PrintChainAsync($"OriginalPath; {routing}", original, fixture, details, output, cancellationToken);
            await PrintChainAsync($"AllMemberships; {routing}", expanded, fixture, details, output, cancellationToken);
        }

        await DemonstrateChainBoundariesAsync(context, fixture, step, details, output, cancellationToken);
    }

    /// <summary>
    /// Demonstrates branching, a detected person cycle, and an explicitly incomplete rejected budget.
    /// </summary>
    private static async Task DemonstrateChainBoundariesAsync(
        UserGroupContext context,
        SupervisorScenarioFixture data,
        SupervisorStep step,
        bool details,
        TextWriter output,
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
            output,
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
            output,
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
            output,
            "Expected rejected request: LimitExceeded; incomplete, no supervisor result may be consumed.",
            cancellationToken);
    }
}
