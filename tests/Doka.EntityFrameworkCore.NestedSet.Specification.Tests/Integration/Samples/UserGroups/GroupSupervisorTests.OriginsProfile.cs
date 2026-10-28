using System.Text;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Varies actual membership origins and routing fields without constructing a second million-node suite.
/// </summary>
public abstract partial class GroupSupervisorTests
{
    private static readonly string[] s_profileFields =
    [
        "Route", "ChannelClass", "ManagerRoute", "RoutingZone",
        "Lane", "Cluster", "Protocol", "Mode",
        "Profile", "Phase", "Zone", "Port",
        "Rack", "Batch", "Firmware", "DeviceProfile",
    ];

    /// <summary>
    /// One selection reader handles 1 through 1,000 own origins and 1 through 16 real routing fields (U58,U63).
    /// </summary>
    /// <param name="origins">The number of own memberships evaluated by the selection reader.</param>
    /// <param name="fields">The number of routing metadata criteria evaluated for each origin.</param>
    [Theory]
    [Trait("Category", "Capacity")]
    [InlineData(1, 1)]
    [InlineData(16, 1)]
    [InlineData(100, 1)]
    [InlineData(1000, 1)]
    [InlineData(1, 4)]
    [InlineData(16, 4)]
    [InlineData(100, 4)]
    [InlineData(1000, 4)]
    [InlineData(1, 16)]
    [InlineData(16, 16)]
    [InlineData(100, 16)]
    [InlineData(1000, 16)]
    public async Task OriginAndRoutingCardinalityKeepsOneScalarSelectionReader(
        int origins,
        int fields
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await CreateAsync(probe, planProbe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await SeedOriginProfileAsync(context, data, origins, fields);
        var request = data.Request(App.SearchStart.IncludeSelf) with
        {
            Groups = new App.NthTypeGroupThenValidate(4, 1),
            Matches = s_profileFields.Take(fields).Select(field => new App.MetadataMatch(
                App.MetadataOwner.SourceUser, field, App.MetadataOwner.CandidateMembership, field)).ToArray(),
        };

        // WHY: Work may grow with actual origins and criteria, but not with every origin multiplied by all
        // other origins. The allowance covers 64 handler requests per input unit plus fixed fixture roots.
        var handlerReadBudget = 64L * (origins * ((long)fields + 1) + data.Groups.Length + data.Users.Length);
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
        App.SupervisorResolutionBatch result;
        try
        {
            result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
                App.SupervisorSelection.RequireUnique, CancellationToken.None);
        }
        catch (Exception)
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine + "-usergroups-failed-origins-" + origins + "-" + fields,
                [App.SupervisorQueries.ResolutionRows(context, data.TenantId, request,
                    App.SupervisorSelection.RequireUnique).ToQueryString(),
                    await EstimatedOriginPlanAsync(context, planProbe, commandIndex)]);

            throw;
        }

        var handlerReads = handlersBefore is null ? null
            : RankingHandlerDelta(handlersBefore, await ReadRankingHandlersAsync(context));

        var commandCount = probe.Commands.Count;
        var readerAdvances = probe.ReaderAdvances;
        if (handlerReads is not null)
        {
            // WHY: Capture original-command counters before EXPLAIN executes the query a second time.
            // Handler requests and iterator output describe different boundaries; retain both raw records.
            var sql = planProbe.Commands[commandIndex];
            var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, commandIndex, Engine);
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine + "-usergroups-origins-" + origins + "-" + fields,
            [
                $"Origins={origins}; criteria={fields}; SQL={Encoding.UTF8.GetByteCount(sql)} B; "
                    + $"SESSION read-request budget={handlerReadBudget}",
                "Actual-command deltas: " + FormatRankingHandlers(handlerReads),
                "Calibration status-only delta: " + FormatRankingHandlers(calibration?.Empty),
                "Calibration fully drained fixture-lookup delta: " + FormatRankingHandlers(calibration?.Lookup),
                sql, plan,
            ]);
        }

        // Assert
        Assert.Equal(origins, result.Origins.Count);
        Assert.All(result.Origins, origin =>
        {
            Assert.Equal(App.SupervisorResolutionStatus.Found, origin.Status);
            Assert.Equal(data.Users[1], Assert.Single(origin.Evidence).CandidateUserId);
            Assert.Equal(0, origin.Evidence[0].Distance);
        });

        Assert.Equal(1, commandCount);
        Assert.Equal(0, probe.Entities);
        // WHY: A successful origin carries its outcome and sole evidence in one row, plus the source marker.
        // The observer also counts a provider's final end-of-stream advance when one is reported.
        Assert.InRange(readerAdvances, origins + 1, origins + 2);
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
                $"Origins={origins}; criteria={fields}; SESSION handler read requests={actualHandlerReads}; "
                    + $"budget={handlerReadBudget}.");

            Assert.InRange(actualHandlerReads, 0, handlerReadBudget);
        }
    }

    /// <summary>Records an estimated failure plan without repeating the timed-out payload operation.</summary>
    /// <param name="context">The context whose open connection supplies the estimated plan.</param>
    /// <param name="probe">The observer holding the original SQL and parameter bindings.</param>
    /// <param name="index">The original command's position in the observer.</param>
    private async Task<string> EstimatedOriginPlanAsync(
        App.UserGroupContext context,
        EnterpriseProbe probe,
        int index
    )
    {
        if (Engine is not ("MySql" or "MariaDb" or "PostgreSql"))
        {
            return "Estimated failure plan is available for server capacity engines only.";
        }

        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = (Engine == "PostgreSql" ? "EXPLAIN (FORMAT JSON) " : "EXPLAIN FORMAT=JSON ")
            + probe.Commands[index];
        foreach (var parameter in probe.ParameterBindings[index])
        {
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var output = new StringBuilder();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            output.AppendLine(reader.GetString(0));
        }

        return output.ToString();
    }

    /// <summary>
    /// The warmed fifth routing criterion stays within the additional scalar-expression allocation budget (U63).
    /// </summary>
    [Fact]
    public async Task OneAdditionalRoutingCriterionHasBoundedWarmBuildCost()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var four = data.Request() with
        {
            Matches = s_profileFields.Take(4).Select(field => new App.MetadataMatch(
                App.MetadataOwner.SourceUser, field, App.MetadataOwner.CandidateMembership, field)).ToArray(),
        };

        var five = four with
        {
            Matches = s_profileFields.Take(5).Select(field => new App.MetadataMatch(
                App.MetadataOwner.SourceUser, field, App.MetadataOwner.CandidateMembership, field)).ToArray(),
        };

        IQueryable<App.SupervisorCandidate>? query = null;
        Func<object> baseline = () => query = App.SupervisorQueries.Candidates(context, data.TenantId, four);
        Func<object> expanded = () => query = App.SupervisorQueries.Candidates(context, data.TenantId, five);
        probe.Enabled = true;
        for (var warmup = 0; warmup < 10; warmup++)
        {
            _ = baseline();
            _ = expanded();
        }

        // Act
        var allocations = QueryConstructionAllocation.MeasurePaired(baseline, expanded);

        // Assert
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Engine={Engine}; four criteria={allocations.Baseline} B/build; five={allocations.Expanded} B/build; "
            + $"additional={allocations.Expanded - allocations.Baseline} B/build.");
        Assert.InRange(allocations.Baseline, 0, 256 * 1024);
        Assert.InRange(allocations.Expanded, 0, 256 * 1024);
        Assert.True(allocations.Expanded - allocations.Baseline <= 4 * 1024,
            $"Four criteria: {allocations.Baseline} B; five: {allocations.Expanded} B; "
            + $"additional: {allocations.Expanded - allocations.Baseline} B.");
        Assert.NotNull(query?.Expression);
        Assert.Empty(probe.Commands);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Builds medium application data around the existing small hierarchy, preserving tenant-qualified foreign keys.
    /// </summary>
    /// <param name="context">The context that persists the profile data.</param>
    /// <param name="data">The existing hierarchy and users extended by the profile.</param>
    /// <param name="origins">The number of new own-membership origins.</param>
    /// <param name="fields">The number of matching routing fields on each origin.</param>
    private async Task SeedOriginProfileAsync(
        App.UserGroupContext context,
        App.SupervisorScenarioFixture data,
        int origins,
        int fields
    )
    {
        await context.UserGroupMemberships
            .Where(member => member.TenantId == data.TenantId && member.UserId == data.Users[0])
            .ExecuteDeleteAsync(CancellationToken.None);
        var root = await context.Groups.Where(group => group.TenantId == data.TenantId && group.Id == data.Groups[0])
            .Select(group => new { group.Right, group.Depth })
            .SingleAsync(CancellationToken.None);

        await context.Groups.Where(group => group.TenantId == data.TenantId && group.Id == data.Groups[0])
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(group => group.Right, root.Right + origins * 2L),
                CancellationToken.None);
        var groups = new App.UserGroup[origins];
        for (var index = 0; index < origins; index++)
        {
            groups[index] = new App.UserGroup
            {
                TenantId = data.TenantId,
                TreeId = data.TreeId,
                Name = $"Profile origin {index}",
                TypeId = 4,
                ParentId = data.Groups[0],
                Left = root.Right + index * 2L,
                Right = root.Right + index * 2L + 1,
                Depth = 1,
                Position = index + 1,
            };
        }

        await context.Groups.AddRangeAsync(groups, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        for (var fieldIndex = 0; fieldIndex < fields; fieldIndex++)
        {
            var field = s_profileFields[fieldIndex];
            if (fieldIndex >= 4)
            {
                await context.UserMetadataDefinitions.AddAsync(new App.UserMetadataDefinition
                {
                    TenantId = data.TenantId,
                    FieldCode = field,
                }, CancellationToken.None);
                await context.MembershipMetadataDefinitions.AddAsync(new App.MembershipMetadataDefinition
                {
                    TenantId = data.TenantId,
                    GroupTypeId = 4,
                    FieldCode = field,
                }, CancellationToken.None);
            }

            if (fieldIndex != 0)
            {
                await context.UserMetadatas.AddAsync(new App.UserMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    FieldCode = field,
                    Value = "lane-blue",
                }, CancellationToken.None);
            }
        }

        foreach (var group in groups)
        {
            await context.UserGroupMemberships.AddRangeAsync([
                new App.UserGroupMembership
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    UserGroupId = group.Id,
                    LeadershipPriority = 50,
                },
                new App.UserGroupMembership
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    UserGroupId = group.Id,
                    LeadershipPriority = 10,
                }], CancellationToken.None);
            await context.MembershipRoles.AddAsync(new App.MembershipRole
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = group.Id,
                RoleId = data.ManagerRoleId,
            }, CancellationToken.None);
            foreach (var field in s_profileFields.Take(fields))
            {
                await context.MembershipMetadatas.AddAsync(new App.MembershipMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    UserGroupId = group.Id,
                    GroupTypeId = 4,
                    FieldCode = field,
                    Value = "lane-blue",
                }, CancellationToken.None);
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await PrepareConsumerStatisticsAsync(context);
    }
}
