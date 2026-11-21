using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionConsumerCapacityProfiles
{
    /// <summary>Measures complete owner/path/UNION/SUM construction and the real two-reader report.</summary>
    internal static async Task<CompositionConsumerCapacityObservation> MeasureSignalsAsync(
        TestDatabase database,
        string engine,
        CompositionConsumerDataset data,
        SignalApp.SignalReportRequest request,
        bool balanced,
        bool deep,
        CancellationToken cancellationToken
    )
    {
        await using var source = database.CreateContext();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        var commands = new CompositionCommandProbe();
        var options = new DbContextOptionsBuilder<CompositionCapacitySignalLabContext>(
                ModelCompatibilityDatabase.Options<CompositionCapacitySignalLabContext>(
                    engine,
                    source.Database.GetConnectionString()!))
            .AddInterceptors(planProbe, commands, TestMaterializationObserver.Instance)
            .Options;

        await using var context = new CompositionCapacitySignalLabContext(options);
        FunctionalTestContextPolicy.Configure(context);
        var materialized = 0;
        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        var validated = SignalApp.SignalReportValidation.Snapshot(request);
        _ = SignalApp.SignalReportBuilder.Build(context, validated);
        var constructionCommands = commands.CommandCount;
        var topology = deep
            ? "chain"
            : balanced
                ? "balanced"
                : "wide";

        var profile = $"signal-{topology}-{data.Nodes}-anchors-{request.AnchorIds.Count}-"
            + $"channels-{request.ChannelIds.Count}-sources-{request.Sources.Count}-"
            + $"{request.Criteria[0].OwnerMode}";

        var started = Stopwatch.GetTimestamp();
        SignalApp.SignalReportCell[] rows;

        try
        {
            rows = await SignalApp.SignalReportBuilder.ReadAsync(context, request, cancellationToken);
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

        var actual = rows
            .Select(SignalKey)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = ExpectedSignals(data, request, balanced, deep);
        var plan = await QueryPlanTestSupport.ExplainAsync(
            context,
            planProbe,
            1,
            engine,
            cancellationToken: cancellationToken);

        var observation = new CompositionConsumerCapacityObservation(
            profile,
            actual,
            expected,
            commandCount,
            2,
            maximumParameters,
            constructionCommands,
            materialized,
            tracked,

            elapsed,
            plan);

        await WriteAsync(engine, observation, planProbe.Commands[1], cancellationToken: cancellationToken);

        return observation;
    }
}
