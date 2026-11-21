namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class GroupSupervisorScenario
{
    /// <summary>Runs the independently selected group-level-supervisors demonstration.</summary>
    private static async Task RunGroupLevelsAsync(
        UserGroupContext context,
        SupervisorScenarioFixture fixture,
        SupervisorRequest request,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await SeedOwnerDemonstrationAsync(context, fixture, cancellationToken);
        SupervisorGroups[] groupSelections = [new NthEligibleGroup([1], 1), new NthTypeGroupThenValidate(1, 1)];

        foreach (var groups in groupSelections)
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
                output,
                cancellationToken);
        }

        (MetadataOwner, MetadataOwner)[] ownerPairs =
        [
            (MetadataOwner.SourceUser, MetadataOwner.CandidateMembership),
            (MetadataOwner.SourceMembership, MetadataOwner.CandidateGroup),
            (MetadataOwner.SourceGroup, MetadataOwner.CandidateUser),
        ];

        foreach (var owners in ownerPairs)
        {
            var matched = await SupervisorQueries.ResolveAsync(
                context,
                fixture.TenantId,
                request with
                {
                    Groups = new NthTypeGroupThenValidate(1, 1),
                    Matches =
                    [
                        request.Matches[0],
                        new MetadataMatch(owners.Item1, "ChannelClass", owners.Item2, "ChannelClass"),
                    ],
                },
                SupervisorSelection.RequireUnique,
                cancellationToken);

            await PrintAsync(
                $"Route AND ChannelClass: {owners.Item1} -> {owners.Item2}",
                matched,
                fixture,
                details,
                output,
                cancellationToken);
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
}
