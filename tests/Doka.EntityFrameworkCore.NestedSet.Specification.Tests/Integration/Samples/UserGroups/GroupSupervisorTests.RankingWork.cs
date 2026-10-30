using System.Text;
using System.Text.Json;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies actual server person ranking against distinct people and retained occurrence evidence.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>Ranks a thousand people without turning duplicate occurrences into additional person slots.</summary>
    /// <param name="profile">The unique, absent, or tied business-rank contract under observation.</param>
    /// <param name="status">The independent expected per-origin outcome.</param>
    /// <param name="winner">The fixture priority index of the unique winner, or minus one when unresolved.</param>
    [Theory]
    [Trait("Category", "Capacity")]
    [InlineData("HighestUnique", "Found", 0)]
    [InlineData("HighestTie", "Ambiguous", -1)]
    [InlineData("HighestAllTie", "Ambiguous", -1)]
    [InlineData("NthMiddle", "Found", 499)]
    [InlineData("NthAbsent", "NotFound", -1)]
    [InlineData("NthTieFirstSlot", "Ambiguous", -1)]
    [InlineData("NthTieSecondSlot", "Ambiguous", -1)]
    public async Task PersonRankingRetainsNativeWorkAndExactOccurrenceEvidence(
        string profile,
        string status,
        int winner
    )
    {
        // Arrange
        const int people = 1_000;
        const int occurrences = 2 * people;
        var probe = new SupervisorCommandProbe();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await CreateAsync(probe, planProbe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var users = await SeedFanInAsync(context, data, people);
        await SeedRankingOccurrencesAsync(context, data, users, profile);
        var request = data.Request(App.SearchStart.IncludeSelf, data.Groups[5]) with
        {
            Roles = new App.NoRoleFilter(), Groups = new App.AllEligibleGroups(), Matches = [],
        };

        var selection = profile switch
        {
            "HighestUnique" or "HighestTie" or "HighestAllTie" => App.SupervisorSelection.HighestBusinessPriority,
            "NthAbsent" => App.SupervisorSelection.NthPerson(people + 1),
            "NthTieSecondSlot" => App.SupervisorSelection.NthPerson(501),
            _ => App.SupervisorSelection.NthPerson(500),
        };

        // WHY: Input-sized limits admit linear enumeration and native display rounding, but not a correlated
        // comparison of every distinct person with the entire candidate occurrence population.
        var linearRankWorkBudget = 2L * (people + occurrences + data.Users.Length + data.Groups.Length);
        var materializedRankOutputBudget = 2L * occurrences;
        // WHY: The pipeline has membership/user/group inputs, per-person reduction, origin witnesses and the
        // final evidence join. Eight input passes with eight handler operations each allow fixed linear work;
        // source/group fixture roots receive 32 additional probes each, never a people-by-occurrences allowance.
        var handlerReadBudget = 64L * occurrences + 32L * (data.Groups.Length + data.Users.Length);
        Dictionary<string, long>? handlersBefore = null;
        (Dictionary<string, long> Empty, Dictionary<string, long> Lookup, int Rows)? calibration = null;
        if (Engine is "MySql" or "MariaDb")
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
            calibration = await CalibrateRankingHandlersAsync(context, data.Users[0]);
            handlersBefore = await ReadRankingHandlersAsync(context);
        }

        var commandIndex = planProbe.Commands.Count;
        probe.Enabled = true;

        // Act
        App.SupervisorResolutionBatch batch;
        try
        {
            batch = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request, selection,
                CancellationToken.None);
        }
        catch (Exception error)
        {
            var failure = new List<string> { error.ToString() };
            if (planProbe.Commands.Count > commandIndex)
            {
                failure.Add(planProbe.Commands[commandIndex]);
                try
                {
                    failure.Add(await EstimatedOriginPlanAsync(context, planProbe, commandIndex));
                }
                catch (Exception captureError)
                {
                    // WHY: Failure diagnostics must retain, rather than replace, the original rank failure.
                    failure.Add("Estimated plan unavailable: " + captureError);
                }
            }

            await QueryPlanTestSupport.WriteEvidenceAsync(Engine + "-usergroups-ranking-failed-" + profile,
                failure);

            throw;
        }

        var handlerReads = handlersBefore is null ? null
            : RankingHandlerDelta(handlersBefore, await ReadRankingHandlersAsync(context));

        var commandCount = probe.Commands.Count;
        var readerAdvances = probe.ReaderAdvances;
        var sql = planProbe.Commands[commandIndex];
        var commandBytes = Encoding.UTF8.GetByteCount(sql);
        var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, commandIndex, Engine);
        await QueryPlanTestSupport.WriteEvidenceAsync(Engine + "-usergroups-ranking-native-" + profile,
        [
            $"People={people}; occurrences={occurrences}; SQL={commandBytes} B; "
                + $"linearWorkBudget={linearRankWorkBudget}; materializedOutputBudget={materializedRankOutputBudget}",
            $"SESSION read-request budget={handlerReadBudget}; actual-command deltas: "
                + FormatRankingHandlers(handlerReads),
            "Calibration status-only delta: " + FormatRankingHandlers(calibration?.Empty),
            "Calibration fully drained fixture-lookup delta: " + FormatRankingHandlers(calibration?.Lookup),
            string.Join(Environment.NewLine, planProbe.ParameterBindings[commandIndex]
                .Select(parameter => parameter.ParameterName + ":" + parameter.DbType + "="
                    + JsonSerializer.Serialize(parameter.Value))),
            sql, plan,
        ]);

        // Assert
        Assert.Equal(App.SupervisorBatchStatus.Completed, batch.Status);
        Assert.True(batch.Complete);
        var origin = Assert.Single(batch.Origins);
        Assert.Equal(status, origin.Status.ToString());
        Assert.Equal(data.Groups[5], origin.StartGroupId);
        Assert.Equal(1, commandCount);
        var expectedReaderRows = winner >= 0 ? 3 : 2;
        Assert.InRange(readerAdvances, expectedReaderRows, expectedReaderRows + 1);
        Assert.InRange(probe.ParameterCounts[0], 0, 999);
        // WHY: Reuse the existing frontier transport ceiling; candidate cardinality belongs in rows, not SQL text.
        Assert.InRange(commandBytes, 1, 2_000_000);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        if (calibration is { } calibrated)
        {
            Assert.Equal(1, calibrated.Rows);
            Assert.True(calibrated.Lookup.Values.Sum() > calibrated.Empty.Values.Sum(),
                "A real fixture lookup must add read requests beyond the SHOW SESSION STATUS control.");
        }

        if (handlerReads is not null)
        {
            var actualHandlerReads = handlerReads.Values.Sum();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Profile={profile}; SESSION handler read requests={actualHandlerReads}; budget={handlerReadBudget}.");
            Assert.InRange(actualHandlerReads, 0, handlerReadBudget);
        }

        if (winner >= 0)
        {
            var expected = ExpectedRankedEvidence(data, users[winner].Id, profile, winner);
            Assert.Equal(expected, origin.Evidence.OrderBy(row => row.Distance));
            Assert.Equal(expected[0], origin.Continuation);
        }
        else
        {
            Assert.Empty(origin.Evidence);
            Assert.Null(origin.Continuation);
        }

        if (Engine == "MySql")
        {
            // WHY: MySQL may fetch ranking witnesses before its displayed iterator execution starts.
            // Iterator tuples alone do not establish total ranking work; the original-command handlers do.
            var work = CompositionPlanWork.ReadMySqlTree(plan);
            Assert.Equal((double)expectedReaderRows, work.RootOutputTuples);
            Assert.InRange(work.MaximumOperatorTuples, 0, linearRankWorkBudget);
            Assert.InRange(work.MaximumMaterializedTuples, 0, materializedRankOutputBudget);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Profile={profile}; actual tuples={work.MaximumOperatorTuples}; "
                + $"materialized output={work.MaximumMaterializedTuples}; SQL={commandBytes} B.");
        }
        else
        {
            // WHY: Other provider plans are retained for native inspection; this shared suite cannot access
            // the MySql-project-only MariaDB reader and must not imply an unmeasured linear work guarantee.
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Profile={profile}; SQL={commandBytes} B; native work retained, not parsed for {Engine}.");
        }
    }

    /// <summary>Adds one farther occurrence per person while preserving independent fixture priority order.</summary>
    private async Task SeedRankingOccurrencesAsync(
        App.UserGroupContext context,
        App.SupervisorScenarioFixture data,
        IReadOnlyList<App.User> users,
        string profile
    )
    {
        var source = data.Users[0];
        await context.UserGroupMemberships.Where(member => member.TenantId == data.TenantId
                && Enumerable.Contains(data.Users, member.UserId) && member.UserId != source)
            .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.Visible, false),
                CancellationToken.None);
        var priorities = users.Select((user, index) => new { user.Id, Priority = RankPriority(profile, index) })
            .ToDictionary(row => row.Id, row => row.Priority);

        var near = await context.UserGroupMemberships.Where(member => member.TenantId == data.TenantId
                && member.UserGroupId == data.Groups[5] && member.UserId != source)
            .ToArrayAsync(CancellationToken.None);

        foreach (var member in near)
        {
            member.LeadershipPriority = priorities[member.UserId];
            await context.UserGroupMemberships.AddAsync(new App.UserGroupMembership
            {
                TenantId = data.TenantId, UserId = member.UserId, UserGroupId = data.Groups[4],
                LeadershipPriority = FarRankPriority(profile, member.LeadershipPriority),
            }, CancellationToken.None);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await PrepareConsumerStatisticsAsync(context);
    }

    /// <summary>Defines priorities by fixture position rather than generated user identities.</summary>
    private static int RankPriority(string profile, int index)
        => profile == "HighestAllTie" || (profile == "HighestTie" && index == 1) ? 1
            : (profile is "NthTieFirstSlot" or "NthTieSecondSlot") && index == 500 ? 500 : index + 1;

    /// <summary>Keeps better-priority farther duplicates behind the nearer canonical Nth-person occurrence.</summary>
    private static int FarRankPriority(string profile, int priority)
        => profile.StartsWith("Nth", StringComparison.Ordinal) ? 1 : 10_000 + priority;

    /// <summary>Retains expected scalar occurrences independently of server or application rank reduction.</summary>
    private static App.SupervisorCandidate[] ExpectedRankedEvidence(
        App.SupervisorScenarioFixture data,
        int user,
        string profile,
        int index
    )
    {
        var priority = RankPriority(profile, index);

        return
        [
            new()
            {
                TenantId = data.TenantId, SourceUserId = data.Users[0], StartGroupId = data.Groups[5],
                StartTreeId = data.TreeId, FoundGroupId = data.Groups[5], CandidateUserId = user,
                Distance = 0, LeadershipPriority = priority,
            },
            new()
            {
                TenantId = data.TenantId, SourceUserId = data.Users[0], StartGroupId = data.Groups[5],
                StartTreeId = data.TreeId, FoundGroupId = data.Groups[4], CandidateUserId = user,
                Distance = 1, LeadershipPriority = FarRankPriority(profile, priority),
            },
        ];
    }
}
