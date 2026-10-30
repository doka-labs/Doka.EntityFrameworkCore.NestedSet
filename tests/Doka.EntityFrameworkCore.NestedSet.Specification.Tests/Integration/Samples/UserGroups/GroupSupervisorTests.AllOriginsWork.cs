using System.Text;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies the direct All-evidence transport independently of unique-person reduction.</summary>
public abstract partial class GroupSupervisorTests
{
    private static readonly int[] s_allOriginTypes = [4];

    /// <summary>Preserves every origin and occurrence without an origins-by-origins transport join.</summary>
    /// <param name="fields">The matching source-user and candidate-membership metadata criteria per origin.</param>
    [Theory]
    [Trait("Category", "Capacity")]
    [InlineData(1)]
    [InlineData(16)]
    public async Task AllOriginsRetainDirectEvidenceAndBoundedNativeWork(int fields)
    {
        // Arrange
        const int origins = 1_000;
        var probe = new SupervisorCommandProbe();
        var plans = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await CreateAsync(probe, plans);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await SeedOriginProfileAsync(context, data, origins, fields);
        var expectedOrigins = await context.UserGroupMemberships
            .Where(member => member.TenantId == data.TenantId && member.UserId == data.Users[0])
            .Select(member => member.UserGroupId).Order().ToArrayAsync(CancellationToken.None);

        var request = data.Request(App.SearchStart.IncludeSelf) with
        {
            Groups = new App.AllEligibleGroups(s_allOriginTypes),
            Matches = s_profileFields.Take(fields).Select(field => new App.MetadataMatch(
                App.MetadataOwner.SourceUser, field, App.MetadataOwner.CandidateMembership, field)).ToArray(),
        };

        // WHY: Reuse the origin/routing profile's fixed linear allowance. Direct All evidence may grow with
        // actual origins and fields, never with every origin compared or joined to all the other origins.
        var linearWorkBudget = 64L * (origins * ((long)fields + 1) + data.Groups.Length + data.Users.Length);
        Dictionary<string, long>? before = null;
        (Dictionary<string, long> Empty, Dictionary<string, long> Lookup, int Rows)? calibration = null;
        if (Engine is "MySql" or "MariaDb")
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
            calibration = await CalibrateRankingHandlersAsync(context, data.Users[0]);
            before = await ReadRankingHandlersAsync(context);
        }

        var index = plans.Commands.Count;
        probe.Enabled = true;

        // Act
        App.SupervisorResolutionBatch result;
        try
        {
            result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
                App.SupervisorSelection.All, CancellationToken.None);
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
                    // WHY: Native diagnostic failure must not replace the original transport failure.
                    failure.Add("Estimated plan unavailable: " + captureError);
                }
            }

            await QueryPlanTestSupport.WriteEvidenceAsync(
                $"{Engine}-usergroups-all-origins-failed-{fields}", failure);

            throw;
        }

        var handlers = before is null ? null : RankingHandlerDelta(before, await ReadRankingHandlersAsync(context));
        var commandCount = probe.Commands.Count;
        var readerAdvances = probe.ReaderAdvances;
        var sql = plans.Commands[index];
        var bytes = Encoding.UTF8.GetByteCount(sql);
        var plan = await QueryPlanTestSupport.ExplainAsync(context, plans, index, Engine);
        await QueryPlanTestSupport.WriteEvidenceAsync($"{Engine}-usergroups-all-origins-{fields}",
        [
            $"Origins={origins}; fields={fields}; SQL={bytes} B; linearWorkBudget={linearWorkBudget}",
            "Actual-command SESSION read requests: " + FormatRankingHandlers(handlers),
            "Calibration status-only delta: " + FormatRankingHandlers(calibration?.Empty),
            "Calibration fully drained fixture-lookup delta: " + FormatRankingHandlers(calibration?.Lookup),
            sql, plan,
        ]);

        // Assert
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        Assert.True(result.Complete);
        Assert.Equal(origins, expectedOrigins.Length);
        Assert.Equal(expectedOrigins.Select(key => (int?)key),
            result.Origins.Select(origin => origin.StartGroupId).Order());
        Assert.All(result.Origins, origin =>
        {
            Assert.Equal(App.SupervisorResolutionStatus.Found, origin.Status);
            Assert.Null(origin.Continuation);
            var evidence = Assert.Single(origin.Evidence);
            Assert.Equal(data.TenantId, evidence.TenantId);
            Assert.Equal(data.Users[0], evidence.SourceUserId);
            Assert.Equal(origin.StartGroupId, evidence.StartGroupId);
            Assert.Equal(data.TreeId, evidence.StartTreeId);
            Assert.Equal(origin.StartGroupId, evidence.FoundGroupId);
            Assert.Equal(data.Users[1], evidence.CandidateUserId);
            Assert.Equal(0, evidence.Distance);
            Assert.Equal(10, evidence.LeadershipPriority);
        });

        Assert.Equal(1, commandCount);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.InRange(probe.ParameterCounts[0], 0, 999);
        Assert.InRange(bytes, 1, 2_000_000);
        // WHY: All transports one source marker, one marker per original membership, and each direct
        // evidence occurrence. The observer may additionally report the final end-of-stream advance.
        const int expectedRows = 1 + origins + origins;
        Assert.InRange(readerAdvances, expectedRows, expectedRows + 1);
        if (calibration is { } calibrated)
        {
            Assert.Equal(1, calibrated.Rows);
            Assert.True(calibrated.Lookup.Values.Sum() > calibrated.Empty.Values.Sum(),
                "A real fixture lookup must add read requests beyond the SHOW SESSION STATUS control.");
        }

        if (handlers is not null)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"All origins={origins}; fields={fields}; SESSION requests={handlers.Values.Sum()}; "
                + $"budget={linearWorkBudget}.");
            Assert.InRange(handlers.Values.Sum(), 0, linearWorkBudget);
        }

        if (Engine == "MySql")
        {
            // WHY: Small handler deltas cannot rule out a large hash-join intermediate. Native actual
            // iterator tuples and materialized output independently enforce the same input-sized allowance.
            var work = CompositionPlanWork.ReadMySqlTree(plan);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"All fields={fields}; native tuples={work.MaximumOperatorTuples}; "
                + $"materialized output={work.MaximumMaterializedTuples}.");
            Assert.Equal((double)expectedRows, work.RootOutputTuples);
            Assert.InRange(work.MaximumOperatorTuples, 0, linearWorkBudget);
            Assert.InRange(work.MaximumMaterializedTuples, 0, linearWorkBudget);
        }

        if (Engine == "PostgreSql")
        {
            // WHY: A join can reject a quadratic candidate bag without returning those rows. Output and
            // join-filter rejections must both remain inside the unchanged origin/routing allowance.
            var work = CompositionPlanWork.ReadPostgreSqlJson(plan);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"All fields={fields}; native tuples={work.MaximumOperatorTuples}; "
                + $"rejected joins={work.MaximumRejectedJoinTuples}.");
            Assert.Equal((double)expectedRows, work.RootOutputTuples);
            Assert.InRange(work.MaximumOperatorTuples, 0, linearWorkBudget);
            Assert.InRange(work.MaximumRejectedJoinTuples, 0, linearWorkBudget);
        }
    }
}
