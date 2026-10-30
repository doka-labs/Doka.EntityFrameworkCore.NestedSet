using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks complete scalar witness evidence for a group rule that admits at most one found group.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>Single-group selection preserves missing, tied and unique outcomes without a second evidence bag.</summary>
    /// <param name="typeFirst">Whether type ranking precedes candidate qualification.</param>
    /// <param name="profile">The independent person-selection or path-rejection scenario.</param>
    /// <param name="status">The expected explicit origin status.</param>
    /// <param name="winner">The expected fixture person, or minus one for an unresolved origin.</param>
    [Theory]
    [InlineData(true, "unique", "Ambiguous", -1)]
    [InlineData(false, "unique", "Ambiguous", -1)]
    [InlineData(true, "priority", "Found", 1)]
    [InlineData(false, "priority", "Found", 1)]
    [InlineData(true, "second", "Found", 2)]
    [InlineData(false, "second", "Found", 2)]
    [InlineData(true, "missing-slot", "NotFound", -1)]
    [InlineData(false, "missing-slot", "NotFound", -1)]
    [InlineData(true, "priority-tie", "Ambiguous", -1)]
    [InlineData(false, "priority-tie", "Ambiguous", -1)]
    [InlineData(true, "rank-tie", "Ambiguous", -1)]
    [InlineData(false, "rank-tie", "Ambiguous", -1)]
    [InlineData(true, "routing-rejects-first", "Found", 2)]
    [InlineData(false, "routing-rejects-first", "Found", 2)]
    [InlineData(true, "expired-edge", "NotFound", -1)]
    [InlineData(false, "expired-edge", "NotFound", -1)]
    public async Task SingleGroupWitnessRetainsExactPersonEvidence(
        bool typeFirst,
        string profile,
        string status,
        int winner
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        if (profile is "priority-tie" or "rank-tie")
        {
            await context.UserGroupMemberships.Where(member => member.TenantId == data.TenantId
                    && member.UserId == data.Users[2] && member.UserGroupId == data.Groups[4])
                .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.LeadershipPriority, 10),
                    CancellationToken.None);
        }

        if (profile == "routing-rejects-first")
        {
            await context.MembershipMetadatas.Where(value => value.TenantId == data.TenantId
                    && value.UserId == data.Users[1] && value.UserGroupId == data.Groups[4])
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value, "different-route"),
                    CancellationToken.None);
        }

        if (profile == "expired-edge")
        {
            await context.GroupRelationPeriods.Where(period => period.TenantId == data.TenantId
                    && period.ParentId == data.Groups[4] && period.ChildId == data.Groups[5])
                .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.ToDate,
                    new DateOnly(2026, 10, 15)), CancellationToken.None);
        }

        var request = data.Request(group: data.Groups[5]) with
        {
            Groups = typeFirst ? new App.NthTypeGroupThenValidate(3, 1) : new App.NthEligibleGroup([3], 1),
        };

        var selection = profile switch
        {
            "priority" or "priority-tie" => App.SupervisorSelection.HighestBusinessPriority,
            "second" => App.SupervisorSelection.NthPerson(2),
            "missing-slot" => App.SupervisorSelection.NthPerson(3),
            "rank-tie" => App.SupervisorSelection.NthPerson(1),
            _ => App.SupervisorSelection.RequireUnique,
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(
            context, data.TenantId, request, selection, CancellationToken.None);

        // Assert
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        var origin = Assert.Single(result.Origins);
        Assert.Equal(status, origin.Status.ToString());
        Assert.Equal(data.Groups[5], origin.StartGroupId);
        Assert.Equal(winner < 0 ? 0 : 1, origin.Evidence.Count);
        Assert.Equal(winner < 0 ? (int?)null : data.Users[winner], origin.Continuation?.CandidateUserId);
        Assert.All(origin.Evidence, row =>
        {
            Assert.Equal(data.TenantId, row.TenantId);
            Assert.Equal(data.TreeId, row.StartTreeId);
            Assert.Equal(data.Users[0], row.SourceUserId);
            Assert.Equal(data.Groups[5], row.StartGroupId);
            Assert.Equal(data.Groups[4], row.FoundGroupId);
            Assert.Equal(1, row.Distance);
            Assert.Equal(winner == 1 ? 10 : 20, row.LeadershipPriority);
        });
        Assert.Single(probe.Commands);
        Assert.InRange(probe.ReaderAdvances, 2, 3);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A thousand distinct people produce one ambiguous origin instead of a client evidence population.</summary>
    /// <param name="typeFirst">Whether type ranking precedes candidate qualification.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task SingleGroupWitnessKeepsNativeCandidateWorkAndTransportBounded(bool typeFirst)
    {
        // Arrange
        const int people = 1_000;
        var probe = new SupervisorCommandProbe();
        var plans = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await CreateAsync(probe, plans);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await SeedFanInAsync(context, data, people);
        var request = data.Request(App.SearchStart.IncludeSelf, data.Groups[5]) with
        {
            Roles = new App.NoRoleFilter(), Matches = [], OnDay = null,
            Groups = typeFirst ? new App.NthTypeGroupThenValidate(4, 1) : new App.NthEligibleGroup([4], 1),
        };

        Dictionary<string, long>? before = null;
        if (Engine is "MySql" or "MariaDb")
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
            _ = await CalibrateRankingHandlersAsync(context, data.Users[0]);
            before = await ReadRankingHandlersAsync(context);
        }

        var commandIndex = plans.Commands.Count;
        probe.Enabled = true;
        var started = Stopwatch.GetTimestamp();

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        var elapsed = Stopwatch.GetElapsedTime(started);
        var commands = probe.Commands.Count;
        var readerAdvances = probe.ReaderAdvances;
        probe.Enabled = false;
        var handlers = before is null ? null : RankingHandlerDelta(before, await ReadRankingHandlersAsync(context));
        var plan = await QueryPlanTestSupport.ExplainAsync(context, plans, commandIndex, Engine);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            $"{Engine}-usergroups-single-group-witness-{typeFirst}",
            [$"People={people}; elapsed={elapsed.TotalMilliseconds:F3} ms; readers={commands}; "
                + $"readerAdvances={readerAdvances}; SESSION requests={FormatRankingHandlers(handlers)}",
                plans.Commands[commandIndex], plan]);

        // Assert
        Assert.Equal(App.SupervisorResolutionStatus.Ambiguous, Assert.Single(result.Origins).Status);
        Assert.Empty(result.Origins[0].Evidence);
        Assert.Equal(1, commands);
        Assert.InRange(readerAdvances, 2, 3);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        var linearBudget = 64L * (people + data.Groups.Length + data.Users.Length);
        if (handlers is not null)
        {
            Assert.InRange(handlers.Values.Sum(), 0, linearBudget);
        }

        if (Engine == "MySql")
        {
            var work = CompositionPlanWork.ReadMySqlTree(plan);
            Assert.Equal(2D, work.RootOutputTuples);
            Assert.InRange(work.MaximumOperatorTuples, 0, linearBudget);
        }

        if (Engine == "PostgreSql")
        {
            var work = CompositionPlanWork.ReadPostgreSqlJson(plan);
            Assert.Equal(2D, work.RootOutputTuples);
            Assert.InRange(work.MaximumOperatorTuples, 0, linearBudget);
            Assert.InRange(work.MaximumRejectedJoinTuples, 0, linearBudget);
        }
    }
}
