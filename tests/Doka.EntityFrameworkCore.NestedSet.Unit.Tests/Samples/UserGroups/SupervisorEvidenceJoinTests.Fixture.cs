using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.UserGroups;

public sealed partial class SupervisorEvidenceJoinTests
{
    /// <summary>Creates isolated missing, tied, and repeated-person origins using the independent sample fixture.</summary>
    private static async Task PrepareMixedOriginsAsync(
        UserGroupContext context,
        SupervisorScenarioFixture data,
        bool closed,
        bool sharedWinner = false
    )
    {
        await context.MembershipRoles.Where(row => row.TenantId == data.TenantId
                && row.UserId != data.Users[1] && row.UserId != data.Users[6])
            .ExecuteDeleteAsync(CancellationToken.None);
        await context.MembershipMetadatas.Where(row => row.TenantId == data.TenantId
                && row.UserGroupId == data.Groups[6])
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "lane-orange"),
                CancellationToken.None);
        await context.Users.Where(user => user.TenantId == data.TenantId
                && (user.Id == data.Users[5] || sharedWinner && user.Id == data.Users[6]))
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Visible, false), CancellationToken.None);
        var otherWinner = sharedWinner ? data.Users[1] : data.Users[2];
        await context.UserGroupMemberships.AddRangeAsync([
            new UserGroupMembership
            {
                TenantId = data.TenantId, UserId = data.Users[0], UserGroupId = data.Groups[0],
            },
            new UserGroupMembership
            {
                TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[2],
                LeadershipPriority = 1,
            },
            new UserGroupMembership
            {
                TenantId = data.TenantId, UserId = otherWinner, UserGroupId = data.Groups[6],
                LeadershipPriority = 10,
            }], CancellationToken.None);

        await context.MembershipRoles.AddRangeAsync([
            new MembershipRole
            {
                TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[2],
                RoleId = data.ManagerRoleId,
            },
            new MembershipRole
            {
                TenantId = data.TenantId, UserId = otherWinner, UserGroupId = data.Groups[6],
                RoleId = data.ManagerRoleId,
            }], CancellationToken.None);

        await context.MembershipMetadatas.AddRangeAsync([
            new MembershipMetadata
            {
                TenantId = data.TenantId, UserId = data.Users[0], UserGroupId = data.Groups[0],
                GroupTypeId = 1, FieldCode = "Route", Value = "lane-missing",
            },
            new MembershipMetadata
            {
                TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[2],
                GroupTypeId = 1, FieldCode = "Route", Value = "lane-blue",
            },
            new MembershipMetadata
            {
                TenantId = data.TenantId, UserId = otherWinner, UserGroupId = data.Groups[6],
                GroupTypeId = 4, FieldCode = "Route", Value = "lane-orange",
            }], CancellationToken.None);

        // WHY: A second valid role source and overlapping periods are existence witnesses, never extra occurrences.
        await context.UserRoles.AddRangeAsync([
            new UserRole { TenantId = data.TenantId, UserId = data.Users[1], RoleId = data.ManagerRoleId },
            new UserRole { TenantId = data.TenantId, UserId = data.Users[5], RoleId = data.ManagerRoleId }],
            CancellationToken.None);

        await context.GroupRelationPeriods.AddAsync(new GroupRelationPeriod
        {
            TenantId = data.TenantId, ParentId = data.Groups[4], ChildId = data.Groups[5],
            FromDate = new DateOnly(2026, 6, 1),
        }, CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        if (closed)
        {
            await context.GroupRelationPeriods.Where(row => row.TenantId == data.TenantId
                    && row.ParentId == data.Groups[2] && row.ChildId == data.Groups[3])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ToDate,
                    new DateOnly(2026, 10, 15)), CancellationToken.None);
        }

        context.ChangeTracker.Clear();
    }

    /// <summary>Owns the EF service graph so the test observer cannot enter the process-wide singleton cache.</summary>
    private static ServiceProvider CreateServices(SupervisorCommandProbe probe)
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
    private static UserGroupContext CreateContext(IServiceProvider services)
        => new(new DbContextOptionsBuilder<UserGroupContext>(Options()).UseInternalServiceProvider(services).Options);

    /// <summary>Creates the standard sample provider configuration without opening a connection.</summary>
    private static DbContextOptions<UserGroupContext> Options()
        => new DbContextOptionsBuilder<UserGroupContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets()).Options;
}
