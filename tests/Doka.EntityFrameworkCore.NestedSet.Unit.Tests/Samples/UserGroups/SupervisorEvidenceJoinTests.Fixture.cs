using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.UserGroups;

public sealed partial class SupervisorEvidenceJoinTests
{
    /// <summary>
    /// Creates isolated missing, tied, and repeated-person origins using the independent sample fixture.
    /// </summary>
    /// <param name="context">The context used by this fixture operation.</param>
    /// <param name="data">The independent synthetic supervisor fixture.</param>
    /// <param name="closed">Whether the fixture closes the selected relation period.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    /// <param name="sharedWinner">Whether both origin paths share the winning user.</param>
    private static async Task PrepareMixedOriginsAsync(
        UserGroupContext context,
        SupervisorScenarioFixture data,
        bool closed,
        CancellationToken cancellationToken,
        bool sharedWinner = false
    )
    {
        await context
            .MembershipRoles
            .Where(row => row.TenantId == data.TenantId
                && row.UserId != data.Users[1]
                && row.UserId != data.Users[6])
            .ExecuteDeleteAsync(cancellationToken);
        await context
            .MembershipMetadatas
            .Where(row => row.TenantId == data.TenantId
                && row.UserGroupId == data.Groups[6])
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "lane-orange"),
                cancellationToken);
        await context
            .Users
            .Where(user => user.TenantId == data.TenantId
                && (user.Id == data.Users[5] || sharedWinner && user.Id == data.Users[6]))
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Visible, false), cancellationToken);
        var otherWinner = sharedWinner ? data.Users[1] : data.Users[2];
        await context.UserGroupMemberships.AddRangeAsync([
                new UserGroupMembership
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    UserGroupId = data.Groups[0],
                },
                new UserGroupMembership
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    UserGroupId = data.Groups[2],
                    LeadershipPriority = 1,
                },
                new UserGroupMembership
                {
                    TenantId = data.TenantId,
                    UserId = otherWinner,
                    UserGroupId = data.Groups[6],
                    LeadershipPriority = 10,
                },
            ],
            cancellationToken);

        await context.MembershipRoles.AddRangeAsync([
                new MembershipRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    UserGroupId = data.Groups[2],
                    RoleId = data.ManagerRoleId,
                },
                new MembershipRole
                {
                    TenantId = data.TenantId,
                    UserId = otherWinner,
                    UserGroupId = data.Groups[6],
                    RoleId = data.ManagerRoleId,
                },
            ],
            cancellationToken);

        await context.MembershipMetadatas.AddRangeAsync([
                new MembershipMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    UserGroupId = data.Groups[0],
                    GroupTypeId = 1,
                    FieldCode = "Route",
                    Value = "lane-missing",
                },
                new MembershipMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    UserGroupId = data.Groups[2],
                    GroupTypeId = 1,
                    FieldCode = "Route",
                    Value = "lane-blue",
                },
                new MembershipMetadata
                {
                    TenantId = data.TenantId,
                    UserId = otherWinner,
                    UserGroupId = data.Groups[6],
                    GroupTypeId = 4,
                    FieldCode = "Route",
                    Value = "lane-orange",
                },
            ],
            cancellationToken);

        // WHY: A second valid role source and overlapping periods are existence witnesses, never extra occurrences.
        await context.UserRoles.AddRangeAsync([
                new UserRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    RoleId = data.ManagerRoleId,
                },
                new UserRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[5],
                    RoleId = data.ManagerRoleId,
                },
            ],
            cancellationToken);

        await context.GroupRelationPeriods.AddAsync(new GroupRelationPeriod
            {
                TenantId = data.TenantId,
                ParentId = data.Groups[4],
                ChildId = data.Groups[5],
                FromDate = new DateOnly(2026, 6, 1),
            },
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        if (closed)
        {
            await context
                .GroupRelationPeriods
                .Where(row => row.TenantId == data.TenantId
                    && row.ParentId == data.Groups[2]
                    && row.ChildId == data.Groups[3])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ToDate,
                        new DateOnly(2026, 10, 15)),
                    cancellationToken);
        }

        context.ChangeTracker.Clear();
    }

    /// <summary>Owns the EF service graph so the test observer cannot enter the process-wide singleton cache.</summary>
    private static ServiceProvider CreateServices(
        SupervisorCommandProbe probe
    )
    {
        var services = new ServiceCollection();
        foreach (var extension in Options().Extensions)
        {
            extension.ApplyServices(services);
        }

        services.AddScoped<IInterceptor>(_ => probe);

        return services.BuildServiceProvider();
    }

    /// <summary>Uses the actual sample context with the test-owned interceptor scope.</summary>
    private static UserGroupContext CreateContext(
        IServiceProvider services
    )
        => new(new DbContextOptionsBuilder<UserGroupContext>(Options())
            .UseInternalServiceProvider(services)
            .Options);

    /// <summary>Creates the standard sample provider configuration without opening a connection.</summary>
    private static DbContextOptions<UserGroupContext> Options()
        => new DbContextOptionsBuilder<UserGroupContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;
}
