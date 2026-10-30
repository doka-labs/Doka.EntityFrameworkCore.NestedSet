using System.Text;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies group ordinals independently of the number of matching people in each group.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>Ranks groups once per origin while preserving every person in the selected group.</summary>
    /// <param name="typeFirst">Whether structural type selection precedes application qualification.</param>
    /// <param name="number">The requested one-based group ordinal.</param>
    /// <param name="found">Whether the two-level fixture contains that ordinal.</param>
    [Theory]
    [Trait("Category", "Capacity")]
    [InlineData(true, 2, true)]
    [InlineData(true, 3, false)]
    [InlineData(false, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(false, 3, false)]
    public async Task GroupOrdinalsKeepNativeWorkBoundedByOriginsAndOccurrences(
        bool typeFirst,
        int number,
        bool found
    )
    {
        // Arrange
        const int origins = 1_000;
        const int candidateOccurrences = 4 * origins;
        var probe = new SupervisorCommandProbe();
        var plans = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await CreateAsync(probe, plans);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await SeedGroupRankOriginsAsync(context, data, origins, typeFirst);
        var request = data.Request(App.SearchStart.IncludeSelf) with
        {
            Groups = typeFirst ? new App.NthTypeGroupThenValidate(4, number)
                : new App.NthEligibleGroup([], number),
        };

        // WHY: Reuse the established 64-operation linear pipeline allowance for origins and complete candidate
        // occurrences. It admits fixed aggregate/join passes, never an origins-by-origins ranking comparison.
        var handlerBudget = 64L * (origins + candidateOccurrences)
            + 32L * (data.Groups.Length + data.Users.Length);

        Dictionary<string, long>? before = null;
        if (Engine is "MySql" or "MariaDb")
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
            _ = await CalibrateRankingHandlersAsync(context, data.Users[0]);
            before = await ReadRankingHandlersAsync(context);
        }

        var index = plans.Commands.Count;
        probe.Enabled = true;

        // Act
        App.SupervisorResolutionBatch result;
        try
        {
            result = await App.SupervisorQueries.ResolveAsync(
                context, data.TenantId, request, App.SupervisorSelection.All, CancellationToken.None);
        }
        catch (Exception error)
        {
            var failure = new List<string> { error.ToString() };
            if (plans.Commands.Count > index)
            {
                failure.Add(plans.Commands[index]);
                try
                {
                    failure.Add(await EstimatedOriginPlanAsync(context, plans, index));
                }
                catch (Exception captureError)
                {
                    // WHY: A secondary diagnostic failure must not replace the original query failure.
                    failure.Add("Estimated plan unavailable: " + captureError);
                }
            }

            await QueryPlanTestSupport.WriteEvidenceAsync(
                $"{Engine}-usergroups-group-rank-failed-{typeFirst}-{number}", failure);

            throw;
        }

        var handlers = before is null ? null : RankingHandlerDelta(before, await ReadRankingHandlersAsync(context));
        var readerAdvances = probe.ReaderAdvances;
        var commandCount = probe.Commands.Count;
        var bytes = Encoding.UTF8.GetByteCount(plans.Commands[index]);
        var plan = await QueryPlanTestSupport.ExplainAsync(context, plans, index, Engine);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            $"{Engine}-usergroups-group-rank-{typeFirst}-{number}",
            [$"Origins={origins}; occurrences={candidateOccurrences}; handlerBudget={handlerBudget}; SQL={bytes} B",
                FormatRankingHandlers(handlers), plans.Commands[index], plan]);

        // Assert
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        Assert.Equal(origins, result.Origins.Count);
        Assert.All(result.Origins, origin =>
        {
            Assert.Equal(found ? App.SupervisorResolutionStatus.Found : App.SupervisorResolutionStatus.NotFound,
                origin.Status);
            Assert.Equal(found ? 2 : 0, origin.Evidence.Count);
            Assert.Null(origin.Continuation);
            var expectedPeople = number == 1
                ? new[] { data.Users[1], data.Users[2] } : new[] { data.Users[3], data.Users[5] };

            Assert.Equal(found ? expectedPeople.Order() : [],
                origin.Evidence.Select(row => row.CandidateUserId).Order());
            Assert.All(origin.Evidence, row =>
            {
                Assert.Equal(number == 1 ? origin.StartGroupId : data.Groups[0], row.FoundGroupId);
                Assert.Equal(number - 1, row.Distance);
                Assert.Equal(data.TreeId, row.StartTreeId);
            });
        });

        Assert.Equal(1, commandCount);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        // WHY: All transports one source marker, every origin marker, and every actual evidence occurrence.
        var expectedRows = 1 + origins + (found ? 2 * origins : 0);
        Assert.InRange(readerAdvances, expectedRows, expectedRows + 1);
        if (Engine == "MySql")
        {
            // WHY: A hash join can emit a quadratic intermediate with few handler requests. Actual
            // iterator output and materialized output are independent guards, not substitutes for requests.
            var work = CompositionPlanWork.ReadMySqlTree(plan);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Native tuples={work.MaximumOperatorTuples}; materialized={work.MaximumMaterializedTuples}.");
            Assert.Equal((double)expectedRows, work.RootOutputTuples);
            Assert.InRange(work.MaximumOperatorTuples, 0, handlerBudget);
            Assert.InRange(work.MaximumMaterializedTuples, 0, 2L * candidateOccurrences);
        }

        if (Engine == "PostgreSql")
        {
            // WHY: PostgreSQL can reject a quadratic bag in a join without emitting those rows. Validate
            // both cumulative operator output and join-filter rejections against the original linear budget.
            var work = CompositionPlanWork.ReadPostgreSqlJson(plan);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Native tuples={work.MaximumOperatorTuples}; rejected joins={work.MaximumRejectedJoinTuples}.");
            Assert.Equal((double)expectedRows, work.RootOutputTuples);
            Assert.InRange(work.MaximumOperatorTuples, 0, handlerBudget);
            Assert.InRange(work.MaximumRejectedJoinTuples, 0, handlerBudget);
        }

        if (handlers is not null)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"TypeFirst={typeFirst}; number={number}; handlers={handlers.Values.Sum()}; budget={handlerBudget}.");
            Assert.InRange(handlers.Values.Sum(), 0, handlerBudget);
        }
    }

    /// <summary>Adds two people in both group levels without duplicating their membership or role keys.</summary>
    private async Task SeedGroupRankOriginsAsync(
        App.UserGroupContext context,
        App.SupervisorScenarioFixture data,
        int origins,
        bool typeFirst
    )
    {
        await SeedOriginProfileAsync(context, data, origins, 1);
        if (typeFirst)
        {
            await context.Groups.Where(group => group.TenantId == data.TenantId && group.Id == data.Groups[0])
                .ExecuteUpdateAsync(setters => setters.SetProperty(group => group.TypeId, 4), CancellationToken.None);
            await context.MembershipMetadatas.Where(value => value.TenantId == data.TenantId
                    && value.UserGroupId == data.Groups[0])
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.GroupTypeId, 4),
                    CancellationToken.None);
        }

        var groups = await context.UserGroupMemberships.Where(member => member.TenantId == data.TenantId
                && member.UserId == data.Users[0])
            .Select(member => member.UserGroupId).ToArrayAsync(CancellationToken.None);

        foreach (var group in groups)
        {
            AddGroupRankPerson(context, data, data.Users[2], group, 4);
            context.GroupRelationPeriods.Add(new App.GroupRelationPeriod
            {
                TenantId = data.TenantId, ParentId = data.Groups[0], ChildId = group,
                FromDate = new DateOnly(2026, 1, 1),
            });
        }

        AddGroupRankPerson(context, data, data.Users[3], data.Groups[0], typeFirst ? 4 : 1);
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        // WHY: The rank fixture adds memberships, grants, routing and periods after the origin seed's
        // statistics pass. Prepare the completed population before measuring its native query plan.
        await PrepareConsumerStatisticsAsync(context);
    }

    /// <summary>Stages one exact membership, its local role, and matching typed routing field.</summary>
    private static void AddGroupRankPerson(
        App.UserGroupContext context,
        App.SupervisorScenarioFixture data,
        int user,
        int group,
        int type
    )
    {
        context.UserGroupMemberships.Add(new App.UserGroupMembership
        {
            TenantId = data.TenantId, UserId = user, UserGroupId = group, LeadershipPriority = 10,
        });

        context.MembershipRoles.Add(new App.MembershipRole
        {
            TenantId = data.TenantId, UserId = user, UserGroupId = group, RoleId = data.ManagerRoleId,
        });

        context.MembershipMetadatas.Add(new App.MembershipMetadata
        {
            TenantId = data.TenantId, UserId = user, UserGroupId = group, GroupTypeId = type,
            FieldCode = "Route", Value = "lane-blue",
        });
    }
}
