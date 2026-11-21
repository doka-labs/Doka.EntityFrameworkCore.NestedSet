using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class UserGroupChainBenchmarkTests
{
    /// <summary>Several occurrences of one person retain their evidence without becoming several people.</summary>
    /// <param name="all">Whether every occurrence or its unique-person continuation is returned.</param>
    /// <param name="limit">The exact physical and logical row budget.</param>
    /// <param name="complete">Whether the logical output fits without truncation.</param>
    [Theory]
    [InlineData(false, 4, true)]
    [InlineData(true, 6, true)]
    [InlineData(true, 5, false)]
    public async Task RunApiAsync_SamePersonAtSeveralGroups_PreservesEvidenceAndSmallReaderBudget(
        bool all,
        int limit,
        bool complete
    )
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops4Fanout1);
        benchmark.Initialize();
        await benchmark.PrepareAsync(TestContext.Current.CancellationToken);
        var context = benchmark.Context;
        var tenant = benchmark.Data.Tenant;
        var person = benchmark.Data.People[1];
        var group = benchmark.Data.Groups[6];
        var step = benchmark.Request.Steps[0];
        var role = Assert.IsType<MembershipRoleFilter>(step.Roles).RoleId;
        var route = await context
            .MembershipMetadatas
            .Where(value => value.TenantId == tenant && value.UserId == person)
            .Select(value => value.Value)
            .SingleAsync(TestContext.Current.CancellationToken);

        await context
            .MembershipRoles
            .Where(value => value.TenantId == tenant && value.UserId != person)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        context.UserGroupMemberships.Add(new UserGroupMembership
        {
            TenantId = tenant,
            UserId = person,
            UserGroupId = group,
            LeadershipPriority = 10,
        });

        context.MembershipRoles.Add(new MembershipRole
        {
            TenantId = tenant,
            UserId = person,
            UserGroupId = group,
            RoleId = role,
        });

        context.MembershipMetadatas.Add(new MembershipMetadata
        {
            TenantId = tenant,
            UserId = person,
            UserGroupId = group,
            GroupTypeId = 1,
            FieldCode = "Route",
            Value = route,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        var selection = all
            ? SupervisorSelection.All
            : SupervisorSelection.RequireUnique;

        var search = new SupervisorRequest
        {
            SourceUserId = benchmark.Request.SourceUserId,
            StartGroupId = benchmark.Request.StartGroupId,
            SearchStart = step.SearchStart,
            Roles = step.Roles,
            Groups = new AllEligibleGroups([1]),
            Matches = step.Matches,
            OnDay = step.OnDay,
        };

        var request = benchmark.Request with
        {
            Steps = [step with
            {
                Groups = new AllEligibleGroups([1]),
                Selection = selection,
            }],
            // WHY: All retains two path nodes plus two two-row terminal results; the reader needs four rows.
            MaxResultRows = limit,
        };

        // Act
        var rows = await ReferenceRows(context, tenant, search, selection)
            .Cast<object>()
            .ToArrayAsync(TestContext.Current.CancellationToken);

        var actual = await benchmark.RunApiAsync(request, TestContext.Current.CancellationToken);
        var reference = await UserGroupChainReference.FollowAsync(
            context,
            tenant,
            request,
            true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(rows, row => ReferenceRowKind(row) == 0);
        Assert.Single(rows, row => ReferenceRowKind(row) == 1);
        Assert.Equal(2, rows.Count(row => ReferenceRowKind(row) == 2));
        Assert.Equal(UserGroupChainBenchmarks.Signature(actual), UserGroupChainBenchmarks.Signature(reference));
        Assert.Equal(complete, reference.Complete);
        Assert.Empty(context.ChangeTracker.Entries());

        if (!complete)
        {
            Assert.Contains(reference.Branches, branch => branch.Status == SupervisorChainStatus.LimitExceeded);

            return;
        }

        Assert.Equal(all ? 2 : 1, reference.Branches.Count);
        Assert.All(
            reference.Branches,
            branch =>
            {
                Assert.Equal(SupervisorChainStatus.ReachedRequestedHop, branch.Status);
                Assert.Equal(person, Assert.Single(branch.Hops).Evidence.CandidateUserId);
            });
    }

    /// <summary>Counts source/catalog, origin, and evidence transport independently of logical path output.</summary>
    private static int ReferenceRowKind(
        object row
    ) => (int)row.GetType().GetProperty("Kind")!.GetValue(row)!;
}
