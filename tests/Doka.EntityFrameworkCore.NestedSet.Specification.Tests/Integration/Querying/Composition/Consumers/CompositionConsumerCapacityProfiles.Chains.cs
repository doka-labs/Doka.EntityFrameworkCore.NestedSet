using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionConsumerCapacityProfiles
{
    /// <summary>Measures finite real frontier expansion on the persisted deep hierarchy.</summary>
    private static async Task<CompositionConsumerCapacityObservation> MeasureChainAsync(
        TestDatabase database, string engine, CompositionConsumerDataset data, int hops, bool expanded)
    {
        await using var source = database.CreateContext();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        var commands = new CompositionCommandProbe();
        var options = new DbContextOptionsBuilder<CompositionCapacityGroupContext>(
                ModelCompatibilityDatabase.Options<CompositionCapacityGroupContext>(engine,
                    source.Database.GetConnectionString()!))
            .AddInterceptors(planProbe, commands, TestMaterializationObserver.Instance).Options;

        await using var context = new CompositionCapacityGroupContext(options);
        var materialized = 0;
        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        var steps = Enumerable.Range(0, hops).Select(index => new App.SupervisorStep(
            App.SearchStart.ParentsOnly, new App.MembershipRoleFilter(CompositionConsumerModel.Identity(201 + index)),
            new App.AllEligibleGroups(1), [], null, App.SupervisorSelection.RequireUnique)).ToArray();

        var request = new App.SupervisorChainRequest
        {
            SourceUserId = CompositionConsumerDataset.SourceUser(1), StartGroupId = data.Nodes,
            Steps = steps, RoutingOrigin = App.RoutingOrigin.CurrentSubject,
            MaxFrontierStates = 512, MaxVisitedStates = 2_048, MaxResultRows = 8_192,
        };

        var first = new App.SupervisorRequest
        {
            SourceUserId = request.SourceUserId, StartGroupId = request.StartGroupId,
            SearchStart = steps[0].SearchStart, Groups = steps[0].Groups,
            Roles = steps[0].Roles, Matches = steps[0].Matches,
        };

        // WHY: Expression construction and finite chain execution are separate costs. This number measures
        // the actual initial query, while command/output assertions cover the complete 2/4/8-hop operation.
        var buildBytes = Construction(() => App.SupervisorQueries.Candidates(
            context, CompositionConsumerModel.Tenant, first));

        var constructionCommands = commands.CommandCount;
        var started = Stopwatch.GetTimestamp();
        var result = expanded
            ? await App.SupervisorQueries.FollowAllMembershipsAsync(
                context, CompositionConsumerModel.Tenant, request, CancellationToken.None)
            : await App.SupervisorQueries.FollowOriginalPathAsync(
                context, CompositionConsumerModel.Tenant, request, CancellationToken.None);

        var elapsed = Stopwatch.GetElapsedTime(started);
        var commandCount = commands.CommandCount;
        var maximumParameters = planProbe.ParameterBindings.Max(bindings => bindings.Length);
        var tracked = context.ChangeTracker.Entries().Count();
        var actual = result.Branches.Select(branch =>
                $"{branch.Status}:{branch.InitialGroupId}:" + string.Join(';', branch.Hops.Select(HopKey)))
            .Order(StringComparer.Ordinal).Prepend($"{result.Status}:{result.Complete}").ToArray();

        var expected = ExpectedChains(data.Nodes, hops, expanded);
        var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, 0, engine);
        var profile = $"groups-chain-{data.Nodes}-hops-{hops}-{(expanded ? "expanded" : "original")}-initial-build";
        var observation = new CompositionConsumerCapacityObservation(profile, actual, expected, commandCount,
            hops, maximumParameters, constructionCommands, materialized, tracked, buildBytes,
            256 * 1_024, elapsed, plan);

        await WriteAsync(engine, observation, planProbe.Commands[0]);

        // WHY: Later hops use different current subjects, origins and role parameters. The first reader's
        // plan cannot establish their database work, even when all readers reuse a compiled query shape.
        // Capture every exact command after timing; diagnostic replay never contributes to consumer duration.
        for (var reader = 0; reader < commandCount; reader++)
        {
            var readerPlan = reader == 0 ? plan
                : await QueryPlanTestSupport.ExplainAsync(context, planProbe, reader, engine);

            await WriteAsync(engine, observation with
            {
                Profile = $"{profile}-reader-{reader + 1}", Plan = readerPlan,
            }, planProbe.Commands[reader]);
        }

        return observation;
    }

    /// <summary>Enumerates bounded logical branches independently of the SQL and reducer.</summary>
    private static string[] ExpectedChains(int nodes, int hops, bool expanded)
    {
        var paths = expanded ? 1 << (hops - 1) : 1;
        var expected = new List<string>(paths);

        for (var path = 0; path < paths; path++)
        {
            var rows = new List<string>(hops);

            for (var hop = 1; hop <= hops; hop++)
            {
                var start = hop == 1 || expanded && (path & (1 << (hop - 2))) != 0
                    ? nodes : nodes - hop + 1;

                var source = hop == 1 ? CompositionConsumerDataset.SourceUser(1) : 198 + hop;
                var evidence = new App.SupervisorCandidate
                {
                    TenantId = CompositionConsumerModel.Tenant, SourceUserId = source, StartGroupId = start,
                    StartTreeId = CompositionConsumerModel.Tree, FoundGroupId = nodes - hop,
                    CandidateUserId = 199 + hop, Distance = start - nodes + hop, LeadershipPriority = 1,
                };

                rows.Add(HopKey(new App.SupervisorHop(hop, CompositionConsumerDataset.SourceUser(1),
                    nodes, CompositionConsumerModel.Tree, evidence)));
            }

            expected.Add($"ReachedRequestedHop:{nodes}:" + string.Join(';', rows));
        }

        return expected.Order(StringComparer.Ordinal).Prepend("Completed:True").ToArray();
    }

    /// <summary>Retains every scalar provenance and cursor field relevant to continuation and deduplication.</summary>
    private static string HopKey(App.SupervisorHop hop)
    {
        var row = hop.Evidence;

        return $"{hop.Number},{hop.InitialUserId},{hop.InitialGroupId},{hop.InitialTreeId:N},"
            + $"{CompositionConsumerModel.Integer(row.TenantId)},{row.SourceUserId},{row.StartGroupId},"
            + $"{row.StartTreeId:N},{row.FoundGroupId},{row.CandidateUserId},{row.Distance},{row.LeadershipPriority}";
    }
}
