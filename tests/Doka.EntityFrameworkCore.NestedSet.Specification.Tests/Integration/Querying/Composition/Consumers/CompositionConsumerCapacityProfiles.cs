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
        TestDatabase database,
        string engine,
        int nodes,
        bool balanced,
        bool deep,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pending = s_data.GetValue(
            database,
            owned => new Lazy<Task<CompositionConsumerDataset>>(() => CompositionConsumerDataset.CreateAsync(
                owned,
                engine,
                nodes,
                deep,
                cancellationToken: cancellationToken)));

        // WHY: Shared initialization must reach its terminal state before callers can release the database.
        // A separately canceled wait could leave its seeding commands running during fixture disposal.
        var data = await pending.Value;
        cancellationToken.ThrowIfCancellationRequested();

        if (data.Nodes != nodes
            || data.Deep != deep)
        {
            throw new ArgumentException(
                "One capacity database must retain its original structural population.",
                nameof(nodes));
        }

        var observations = new List<CompositionConsumerCapacityObservation>();

        foreach (var profile in new[]
            {
                (Anchors: 1, Metrics: 1, Sources: 1, Days: 1, Nearest: false),
                (Anchors: 4, Metrics: 2, Sources: 3, Days: 2, Nearest: false),
                (Anchors: 16, Metrics: 2, Sources: 3, Days: 2, Nearest: true),
            })
        {
            observations.Add(
                await MeasureSignalsAsync(
                    database,
                    engine,
                    data,
                    data.Report(profile.Anchors, profile.Metrics, profile.Sources, profile.Days, profile.Nearest),
                    balanced,
                    deep,
                    cancellationToken: cancellationToken));
        }

        var originCounts = deep
            ? CompositionConsumerDataset.OriginCounts.Take(1)
            : CompositionConsumerDataset.OriginCounts;

        foreach (var origins in originCounts)
        {
            var fields = origins == 1
                ? 1
                : origins == 16
                    ? 4
                    : 16;

            observations.Add(
                await MeasureGroupsAsync(
                    database,
                    engine,
                    data,
                    origins,
                    fields,
                    balanced,
                    deep,
                    cancellationToken: cancellationToken));
        }

        if (deep)
        {
            foreach (var hops in new[] { 2, 4, 8 })
            {
                observations.Add(
                    await MeasureChainAsync(
                        database,
                        engine,
                        data,
                        hops,
                        false,
                        cancellationToken: cancellationToken));

                observations.Add(
                    await MeasureChainAsync(
                        database,
                        engine,
                        data,
                        hops,
                        true,
                        cancellationToken: cancellationToken));
            }

            observations.AddRange(
                await MeasureGroupRanksAsync(
                    database,
                    engine,
                    data,
                    cancellationToken: cancellationToken));
        }

        return observations;
    }

    /// <summary>Applies deterministic consumer budgets after all operations have completed.</summary>
    internal static void AssertAll(
        IReadOnlyList<CompositionConsumerCapacityObservation> observations,
        ITestOutputHelper output
    )
    {
        Assert.NotEmpty(observations);

        foreach (var observation in observations)
        {
            output.WriteLine(
                $"{observation.Profile}; commands={observation.Commands}; rows={observation.Actual.Length}; "
                + $"parameters={observation.MaximumParameters}; "
                + $"entities={observation.Materialized}; "
                + FormattableString.Invariant($"elapsed={observation.Elapsed.TotalMilliseconds:F3} ms"));

            Assert.Equal(observation.Expected, observation.Actual);
            Assert.Equal(observation.ExpectedCommands, observation.Commands);
            Assert.InRange(observation.MaximumParameters, 0, 999);
            Assert.Equal(0, observation.ConstructionCommands);
            Assert.Equal(0, observation.Materialized);
            Assert.Equal(0, observation.Tracked);
            Assert.False(string.IsNullOrWhiteSpace(observation.Plan));
        }
    }

}
