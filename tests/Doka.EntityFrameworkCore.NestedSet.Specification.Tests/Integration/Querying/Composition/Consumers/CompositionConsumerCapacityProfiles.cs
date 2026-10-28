using System.Text.Json;
using GroupApp = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;
using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Measures actual sample builders against the same structural rows as generic capacity profiles.</summary>
internal static partial class CompositionConsumerCapacityProfiles
{
    private const string ModelBoundary =
        "Read-only shared structural/period views, fixed classification and visibility; "
        + "not physical period-index or migration evidence.";

    private static readonly ConditionalWeakTable<TestDatabase, Lazy<Task<CompositionConsumerDataset>>> s_data = new();

    /// <summary>Adds bounded payload once, then measures it against the persisted topology.</summary>
    /// <remarks>
    /// Structural views are read-only adaptations, not a claim that the library supports view writes. Fixed
    /// TypeId/visibility and projected edge periods qualify the real builders' structural scaling; ordinary
    /// sample-model tests separately qualify physical constraints, indexes, catalogs and migrations.
    /// </remarks>
    internal static async Task<IReadOnlyList<CompositionConsumerCapacityObservation>> MeasureAsync(
        TestDatabase database, string engine, int nodes, bool balanced = false, bool deep = false)
    {
        var pending = s_data.GetValue(database, owned => new Lazy<Task<CompositionConsumerDataset>>(
            () => CompositionConsumerDataset.CreateAsync(owned, engine, nodes, deep)));

        var data = await pending.Value;

        if (data.Nodes != nodes || data.Deep != deep)
        {
            throw new ArgumentException(
                "One capacity database must retain its original structural population.", nameof(nodes));
        }

        var observations = new List<CompositionConsumerCapacityObservation>();

        foreach (var profile in new[] { (Anchors: 1, Metrics: 1, Sources: 1, Days: 1, Nearest: false),
                     (Anchors: 4, Metrics: 2, Sources: 3, Days: 2, Nearest: false),
                     (Anchors: 16, Metrics: 2, Sources: 3, Days: 2, Nearest: true) })
        {
            observations.Add(await MeasureSignalsAsync(database, engine, data,
                data.Report(profile.Anchors, profile.Metrics, profile.Sources, profile.Days, profile.Nearest),
                balanced, deep));
        }

        var originCounts = deep ? CompositionConsumerDataset.OriginCounts.Take(1)
            : CompositionConsumerDataset.OriginCounts;

        foreach (var origins in originCounts)
        {
            var fields = origins == 1 ? 1 : origins == 16 ? 4 : 16;
            observations.Add(await MeasureGroupsAsync(database, engine, data, origins, fields, balanced, deep));
        }

        if (deep)
        {
            foreach (var hops in new[] { 2, 4, 8 })
            {
                observations.Add(await MeasureChainAsync(database, engine, data, hops, false));
                observations.Add(await MeasureChainAsync(database, engine, data, hops, true));
            }

            observations.AddRange(await MeasureGroupRanksAsync(database, engine, data));
        }

        return observations;
    }

    /// <summary>Applies deterministic consumer budgets after all operations have completed.</summary>
    internal static void AssertAll(
        IReadOnlyList<CompositionConsumerCapacityObservation> observations, ITestOutputHelper output)
    {
        Assert.NotEmpty(observations);

        foreach (var observation in observations)
        {
            output.WriteLine(
                $"{observation.Profile}; commands={observation.Commands}; rows={observation.Actual.Length}; "
                + $"parameters={observation.MaximumParameters}; "
                + $"entities={observation.Materialized}; construction={observation.BuildBytes} B; "
                + FormattableString.Invariant($"elapsed={observation.Elapsed.TotalMilliseconds:F3} ms"));

            Assert.Equal(observation.Expected, observation.Actual);
            Assert.Equal(observation.ExpectedCommands, observation.Commands);
            Assert.InRange(observation.MaximumParameters, 0, 999);
            Assert.Equal(0, observation.ConstructionCommands);
            Assert.Equal(0, observation.Materialized);
            Assert.Equal(0, observation.Tracked);
            Assert.InRange(observation.BuildBytes, 1, observation.BuildBudget);
            Assert.False(string.IsNullOrWhiteSpace(observation.Plan));
        }
    }

    /// <summary>Measures complete owner/path/UNION/SUM construction and the real two-reader report.</summary>
    internal static async Task<CompositionConsumerCapacityObservation> MeasureSignalsAsync(
        TestDatabase database, string engine, CompositionConsumerDataset data,
        SignalApp.SignalReportRequest request, bool balanced, bool deep)
    {
        await using var source = database.CreateContext();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        var commands = new CompositionCommandProbe();
        var options = new DbContextOptionsBuilder<CompositionCapacitySignalLabContext>(
                ModelCompatibilityDatabase.Options<CompositionCapacitySignalLabContext>(engine,
                    source.Database.GetConnectionString()!))
            .AddInterceptors(planProbe, commands, TestMaterializationObserver.Instance).Options;

        await using var context = new CompositionCapacitySignalLabContext(options);
        var materialized = 0;
        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        var validated = SignalApp.SignalReportValidation.Snapshot(request);
        var buildBytes = Construction(() => SignalApp.SignalReportBuilder.Build(context, validated));
        var constructionCommands = commands.CommandCount;
        var topology = deep ? "chain" : balanced ? "balanced" : "wide";
        var profile = $"signal-{topology}-{data.Nodes}-anchors-{request.AnchorIds.Count}-"
            + $"channels-{request.ChannelIds.Count}-sources-{request.Sources.Count}-"
            + $"{request.Criteria[0].OwnerMode}";

        var started = Stopwatch.GetTimestamp();
        SignalApp.SignalReportCell[] rows;

        try
        {
            rows = await SignalApp.SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);
        }
        catch (Exception error)
        {
            await RetainReadFailureAsync(context, engine, profile, planProbe, error);

            throw;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var commandCount = commands.CommandCount;
        var maximumParameters = planProbe.ParameterBindings.Max(bindings => bindings.Length);
        var tracked = context.ChangeTracker.Entries().Count();
        var actual = rows.Select(SignalKey).Order(StringComparer.Ordinal).ToArray();
        var expected = ExpectedSignals(data, request, balanced, deep);
        var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, 1, engine);
        var observation = new CompositionConsumerCapacityObservation(profile, actual, expected, commandCount,
            2, maximumParameters, constructionCommands, materialized, tracked, buildBytes, 256 * 1_024, elapsed, plan);

        await WriteAsync(engine, observation, planProbe.Commands[1]);

        return observation;
    }

    /// <summary>Measures one-reader person selection with increasing origins and routing fields.</summary>
    private static async Task<CompositionConsumerCapacityObservation> MeasureGroupsAsync(
        TestDatabase database, string engine, CompositionConsumerDataset data,
        int origins, int fields, bool balanced, bool deep)
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
        var request = CompositionConsumerDataset.Supervisors(origins, fields);
        var tenant = CompositionConsumerModel.Tenant;
        var topology = deep ? "chain" : balanced ? "balanced" : "wide";
        var profile = $"groups-{topology}-{data.Nodes}-origins-{origins}-matches-{fields}-unique";
        var buildBytes = Construction(() => GroupApp.SupervisorQueries.Candidates(context, tenant, request));
        var constructionCommands = commands.CommandCount;
        var started = Stopwatch.GetTimestamp();
        GroupApp.SupervisorResolutionBatch batch;

        try
        {
            batch = await GroupApp.SupervisorQueries.ResolveAsync(context, tenant, request,
                GroupApp.SupervisorSelection.RequireUnique, CancellationToken.None);
        }
        catch (Exception error)
        {
            await RetainReadFailureAsync(context, engine, profile, planProbe, error);

            throw;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var commandCount = commands.CommandCount;
        var maximumParameters = planProbe.ParameterBindings.Max(bindings => bindings.Length);
        var tracked = context.ChangeTracker.Entries().Count();
        var actual = batch.Origins.Select(origin =>
        {
            var evidence = origin.Evidence.SingleOrDefault();

            return $"{origin.StartGroupId}:{origin.Status}:{evidence?.CandidateUserId}:"
                + $"{evidence?.FoundGroupId}:{evidence?.Distance}:{origin.Evidence.Count}:"
                + $"{evidence?.SourceUserId}:{evidence?.StartTreeId:N}:{evidence?.TenantId:N}:"
                + $"{evidence?.LeadershipPriority}";
        }).Order(StringComparer.Ordinal).Prepend($"{batch.Status}:{batch.Complete}").ToArray();

        var expected = Enumerable.Range(data.Nodes - origins + 1, origins)
            .Select(key => $"{key}:Found:2:1:{Depth(key, balanced, deep)}:1:"
                + $"{CompositionConsumerDataset.SourceUser(origins)}:{CompositionConsumerModel.Tree:N}:"
                + $"{CompositionConsumerModel.Tenant:N}:1")
            .Order(StringComparer.Ordinal).Prepend("Completed:True").ToArray();

        var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, 0, engine);
        var budget = (256 * 1_024) + (Math.Max(0, fields - 4) * 4_096);
        var observation = new CompositionConsumerCapacityObservation(profile, actual, expected, commandCount,
            1, maximumParameters, constructionCommands, materialized, tracked, buildBytes, budget, elapsed, plan);

        await WriteAsync(engine, observation, planProbe.Commands[0]);

        return observation;
    }

    /// <summary>Walks only the known adjacency for independent expected root distances.</summary>
    private static int Depth(int key, bool balanced, bool deep)
    {
        var depth = 0;

        while (CompositionConsumerDataset.Parent(key, balanced, deep) is { } parent)
        {
            depth++;
            key = parent;
        }

        return depth;
    }

    /// <summary>Calculates independent sums with per-day pruning and nearest-owner rejection.</summary>
    private static string[] ExpectedSignals(
        CompositionConsumerDataset data, SignalApp.SignalReportRequest request, bool balanced, bool deep)
    {
        var rows = new List<string>();
        var days = request.UntilExclusive.DayNumber - request.FromInclusive.DayNumber;
        var nearest = request.Criteria[0].OwnerMode == SignalApp.TagOwnerMode.Nearest;

        foreach (var anchorId in request.AnchorIds)
        {
            var anchor = CompositionConsumerModel.Integer(anchorId);

            foreach (var metricId in request.ChannelIds)
            {
                var metric = CompositionConsumerModel.Integer(metricId);

                foreach (var source in request.Sources)
                {
                    var total = 0d;
                    var found = false;

                    foreach (var fact in data.Facts)
                    {
                        if (!CompositionConsumerDataset.Contains(anchor, fact, balanced, deep)
                            || nearest && fact % 2 != 0)
                        {
                            continue;
                        }

                        for (var day = 0; day < days; day++)
                        {
                            if (day > 0 && anchor != 2
                                && CompositionConsumerDataset.Contains(anchor, 2, balanced, deep)
                                && CompositionConsumerDataset.Contains(2, fact, balanced, deep))
                            {
                                continue;
                            }

                            found = true;
                            total += ((metric - 100) * 10 + day + fact % 7) * ((int)source + 1);
                        }
                    }

                    rows.Add(SignalKey(new SignalApp.SignalReportCell(anchorId, metricId, source, found, total)));
                }
            }
        }

        return rows.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Canonicalizes only the small aggregate output, with exact integer-valued sample sums.</summary>
    private static string SignalKey(SignalApp.SignalReportCell row) =>
        $"{CompositionConsumerModel.Integer(row.AnchorId)}:{CompositionConsumerModel.Integer(row.ChannelId)}:"
        + $"{(int)row.Source}:{row.HasData}:" + row.Value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Measures warmed construction separately from request arrays, compilation and execution.</summary>
    private static long Construction(Func<IQueryable> build)
    {
        for (var iteration = 0; iteration < 8; iteration++)
        {
            GC.KeepAlive(build());
        }

        const int operations = 16;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < operations; iteration++)
        {
            GC.KeepAlive(build());
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / operations;
    }

    /// <summary>Retains the failed reader and a nonexecuting estimate without replaying its report.</summary>
    private static async Task RetainReadFailureAsync(
        DbContext context, string engine, string profile, EnterpriseProbe probe, Exception failure)
    {
        var evidence = new List<string> { profile, ModelBoundary, failure.ToString() };
        var index = probe.Commands.Count - 1;

        if (index >= 0)
        {
            evidence.Add(probe.Commands[index]);
        }

        try
        {
            if (index >= 0)
            {
                evidence.AddRange(probe.ParameterBindings[index].Select(parameter =>
                    $"{parameter.ParameterName}; type={parameter.GetType().Name}; dbType={parameter.DbType}; "
                    + $"size={parameter.Size}; value={JsonSerializer.Serialize(parameter.Value)}"));
            }

            if (index >= 0 && (engine is "PostgreSql" or "MySql" or "MariaDb"))
            {
                await context.Database.OpenConnectionAsync(CancellationToken.None);
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = (engine == "PostgreSql" ? "EXPLAIN (FORMAT JSON) " : "EXPLAIN FORMAT=JSON ")
                    + probe.Commands[index];

                // WHY: The original report failed. EXPLAIN without ANALYZE plans that exact command without
                // rerunning its reads, and independent clones retain native array/date/key parameter types.
                foreach (var parameter in probe.ParameterBindings[index])
                {
                    command.Parameters.Add(((ICloneable)parameter).Clone());
                }

                await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

                while (await reader.ReadAsync(CancellationToken.None))
                {
                    evidence.Add(reader.GetString(0));
                }
            }
        }
        catch (Exception diagnosticFailure)
        {
            // WHY: Diagnostic plan failures cannot replace the original command failure or cancellation.
            evidence.Add("Parameter or estimated-plan capture also failed: " + diagnosticFailure);
        }

        try
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                engine + "-composition-consumer-failed-" + profile, evidence);
        }
        catch (Exception diagnosticFailure)
        {
            // WHY: An unavailable evidence directory must also preserve the actual qualification failure.
            failure.Data["ConsumerCapacityEvidenceFailure"] = diagnosticFailure.ToString();
        }
    }

    /// <summary>Retains plans and diagnostic duration without introducing a hardware-dependent release gate.</summary>
    private static Task WriteAsync(
        string engine, CompositionConsumerCapacityObservation observation, string command)
        => QueryPlanTestSupport.WriteEvidenceAsync(engine + "-composition-consumer-" + observation.Profile,
        [
            $"{observation.Profile}; commands={observation.Commands}; rows={observation.Actual.Length}; "
                + $"parameters={observation.MaximumParameters}; "
                + $"entities={observation.Materialized}; construction={observation.BuildBytes} B; "
                + FormattableString.Invariant($"elapsed={observation.Elapsed.TotalMilliseconds:F3} ms"),
            ModelBoundary,
            command,
            observation.Plan,
        ]);
}
