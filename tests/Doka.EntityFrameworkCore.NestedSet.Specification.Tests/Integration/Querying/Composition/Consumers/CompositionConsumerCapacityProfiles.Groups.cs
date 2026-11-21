using GroupApp = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionConsumerCapacityProfiles
{
    /// <summary>Measures one-reader person selection with increasing origins and routing fields.</summary>
    private static async Task<CompositionConsumerCapacityObservation> MeasureGroupsAsync(
        TestDatabase database,
        string engine,
        CompositionConsumerDataset data,
        int origins,
        int fields,
        bool balanced,
        bool deep,
        CancellationToken cancellationToken
    )
    {
        await using var source = database.CreateContext();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        var commands = new CompositionCommandProbe();
        var options = new DbContextOptionsBuilder<CompositionCapacityGroupContext>(
                ModelCompatibilityDatabase.Options<CompositionCapacityGroupContext>(
                    engine,
                    source.Database.GetConnectionString()!))
            .AddInterceptors(planProbe, commands, TestMaterializationObserver.Instance)
            .Options;

        await using var context = new CompositionCapacityGroupContext(options);
        FunctionalTestContextPolicy.Configure(context);
        var materialized = 0;
        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        var request = CompositionConsumerDataset.Supervisors(origins, fields);
        var tenant = CompositionConsumerModel.Tenant;
        var topology = deep
            ? "chain"
            : balanced
                ? "balanced"
                : "wide";

        var profile = $"groups-{topology}-{data.Nodes}-origins-{origins}-matches-{fields}-unique";
        var buildBytes = Construction(() => GroupApp.SupervisorQueries.Candidates(context, tenant, request));
        var constructionCommands = commands.CommandCount;
        var started = Stopwatch.GetTimestamp();
        GroupApp.SupervisorResolutionBatch batch;

        try
        {
            batch = await GroupApp.SupervisorQueries.ResolveAsync(
                context,
                tenant,
                request,
                GroupApp.SupervisorSelection.RequireUnique,
                cancellationToken);
        }
        catch (Exception error)
        {
            // WHY: Failure evidence completes independently of the canceled workload and preserves its error.
            await RetainReadFailureAsync(
                context,
                engine,
                profile,
                planProbe,
                error);

            throw;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var commandCount = commands.CommandCount;
        var maximumParameters = planProbe.ParameterBindings.Max(bindings => bindings.Length);
        var tracked = context
            .ChangeTracker
            .Entries()
            .Count();

        var actual = batch
            .Origins
            .Select(origin =>
            {
                var evidence = origin.Evidence.SingleOrDefault();

                return $"{origin.StartGroupId}:{origin.Status}:{evidence?.CandidateUserId}:"
                    + $"{evidence?.FoundGroupId}:{evidence?.Distance}:{origin.Evidence.Count}:"
                    + $"{evidence?.SourceUserId}:{evidence?.StartTreeId:N}:{evidence?.TenantId:N}:"
                    + $"{evidence?.LeadershipPriority}";
            })
            .Order(StringComparer.Ordinal)
            .Prepend($"{batch.Status}:{batch.Complete}")
            .ToArray();

        var expected = Enumerable
            .Range(data.Nodes - origins + 1, origins)
            .Select(key => $"{key}:Found:2:1:{Depth(key, balanced, deep)}:1:"
                + $"{CompositionConsumerDataset.SourceUser(origins)}:{CompositionConsumerModel.Tree:N}:"
                + $"{CompositionConsumerModel.Tenant:N}:1")
            .Order(StringComparer.Ordinal)
            .Prepend("Completed:True")
            .ToArray();

        var plan = await QueryPlanTestSupport.ExplainAsync(
            context,
            planProbe,
            0,
            engine,
            cancellationToken: cancellationToken);

        var budget = 256 * 1_024 + Math.Max(0, fields - 4) * 4_096;
        var observation = new CompositionConsumerCapacityObservation(
            profile,
            actual,
            expected,
            commandCount,
            1,
            maximumParameters,
            constructionCommands,
            materialized,
            tracked,
            buildBytes,
            budget,
            elapsed,
            plan);

        await WriteAsync(engine, observation, planProbe.Commands[0], cancellationToken: cancellationToken);

        if (engine == "SqlServer")
        {
            // WHY: The fixture has one eligible root manager. Its complete evidence needs these exact
            // adjacency lengths; recomposing every other origin inside each origin is accidental work.
            var requiredEdges = Enumerable
                .Range(data.Nodes - origins + 1, origins)
                .Sum(key => (long)Depth(key, balanced, deep));

            CompositionSqlServerPlanWork.AssertWithinBudget(
                plan,
                CompositionPlanWorkBudget.ForPaths(data.Nodes, origins, requiredEdges),
                profile);
        }

        return observation;
    }
}
