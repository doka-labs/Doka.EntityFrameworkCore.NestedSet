using System.Text.RegularExpressions;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Rejects cheaper benchmark references that omit temporal, visibility, or nearest-owner semantics.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class ConsumerReferenceTests
{
    /// <summary>Nearest selects an owner before testing its values, even when a farther owner would match.</summary>
    /// <param name="nearest">Whether only the nearest typed owner may supply tags.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignalReferencePreservesNearestBeforeValue(
        bool nearest
    )
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var data = await SignalReportDataset.CreateAsync(context, 8, true, CancellationToken.None);
        var request = SignalReportValidation.Snapshot(
            data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any, includeSensor: false));

        // Act
        var actual = await SignalReportBuilder
            .Build(context, request)
            .ToArrayAsync(CancellationToken.None);

        var reference = await SignalReportReference
            .Build(context, request)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(Normalize(actual), Normalize(reference));
        Assert.Equal(nearest ? 0 : 10,
            actual.Where(row => row.AnchorId == SignalReportDataset.Id(1)
                && row.ChannelId == SignalReportDataset.PulseChannel && row.Source == CapturePort.Pulse)
                .Sum(row => row.Value));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A hidden or expired intermediate blocks ancestors while retaining the empty self path.</summary>
    /// <param name="hidden">Whether visibility rather than edge validity blocks the intermediate module.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignalReferencePreservesBranchBarriersAndEmptySelfPath(
        bool hidden
    )
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None);

        if (hidden)
        {
            await context
                .Nodes
                .Where(node => node.TenantId == data.TenantId && node.Id == SignalReportDataset.Id(2))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.IsVisible, false),
                    CancellationToken.None);
        }
        else
        {
            await context
                .LinkAvailability
                .Where(period => period.TenantId == data.TenantId && period.ChildId == SignalReportDataset.Id(3))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(period => period.ToDate, SignalReportDataset.FirstDay.AddDays(-1)),
                    CancellationToken.None);
        }

        var request = SignalReportValidation.Snapshot(data.Request() with { Criteria = [] });

        // Act
        var actual = await SignalReportBuilder
            .Build(context, request)
            .ToArrayAsync(CancellationToken.None);

        var reference = await SignalReportReference
            .Build(context, request)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(Normalize(actual), Normalize(reference));
        Assert.DoesNotContain(actual, row => row.AnchorId == SignalReportDataset.Id(1));
        Assert.Contains(actual, row => row.AnchorId == SignalReportDataset.Id(4)
            && row.ChannelId == SignalReportDataset.PulseChannel && row is { Source: CapturePort.Pulse, Value: 30 });
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Native selection and expression construction require no persisted reporting population.</summary>
    [Fact]
    public async Task SignalConstructionUsesOnlyUnopenedModelMetadata()
    {
        // Arrange
        var configuration = ManualConfig
            .Create(BenchmarkConfiguration.Create(new BenchmarkRunOptions()))
            .AddFilter(new GlobFilter(["*SignalReportConstructionBenchmarks.*"]));

        using var selected = BenchmarkSelection.Discover(configuration, diagnostics: false);
        using var native = BenchmarkConverter.TypeToBenchmarks(
            typeof(SignalReportConstructionBenchmarks),
            configuration);
        var validation = new ValidationParameters(native.BenchmarksCases, native.Config);
        await using var benchmark = new SignalReportConstructionBenchmarks
        {
            Anchors = 16,
            NearestMetadata = true
        };
        benchmark.Initialize();

        // Act
        var actual = benchmark.Api();
        var reference = benchmark.Reference();
        var errors = native
            .Config
            .GetValidators()
            .SelectMany(validator => validator.Validate(validation))
            .Where(error => error.IsCritical)
            .ToArray();

        // Assert
        Assert.False(selected.RequiresDatabase);
        Assert.Empty(selected.DatabaseCases);
        Assert.IsType<IQueryable>(actual, exactMatch: false);
        Assert.IsType<IQueryable>(reference, exactMatch: false);
        Assert.Empty(errors);
    }

    /// <summary>Owner existence retains a scalar SQL boundary instead of joining tags into the event bag.</summary>
    /// <param name="nearest">Whether the owner predicate also retains nearest-before-value semantics.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataOwnersKeepScalarExistenceBoundaries(bool nearest)
    {
        // Arrange
        await using var context = CreateContext();
        var data = new SignalReportDataset(
            SignalReportDataset.Id(1005),
            SignalReportDataset.Id(2005),
            [SignalReportDataset.Id(1)],
            nearest);
        var request = SignalReportValidation.Snapshot(data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any));

        // Act
        var sql = SignalReportReference
            .Build(context, request)
            .ToQueryString();

        // Assert
        Assert.Equal(request.Groups.Length, Regex.Count(sql, @"\)\s*>\s*0\b"));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Distinct events keep their complete grain across overlapping anchors and filtering gates.</summary>
    /// <param name="nearest">Whether nearest typed owners or any matching owners supply the tags.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignalReferencePreservesEqualEventsAndCompleteGrain(bool nearest)
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None);
        var sensor = SignalReportDataset.Id(4);
        var first = SignalReportDataset.FirstDay;
        var pulse = SignalReportDataset.PulseChannel;
        var sweep = SignalReportDataset.SweepChannel;
        (Guid Node, Guid Channel, DateOnly Day, MeasurementGranularity Grain)[] inputs =
        [
            (sensor, pulse, first, MeasurementGranularity.Daily),
            (sensor, pulse, first.AddDays(1), MeasurementGranularity.Daily),
            (sensor, sweep, first, MeasurementGranularity.Daily),
            (sensor, pulse, first, MeasurementGranularity.Monthly),
            (SignalReportDataset.Id(3), pulse, first, MeasurementGranularity.Daily),
            (sensor, pulse, first.AddDays(2), MeasurementGranularity.Daily),
        ];

        var events = inputs
            .Select(input => new PulseRecord
            {
                TenantId = data.TenantId,
                NodeId = input.Node,
                ChannelId = input.Channel,
                Granularity = input.Grain,
                Day = input.Day,
                PulseCount = 10,
            })
            .ToArray();

        context.PulseRecords.AddRange(events);
        context.SweepRecords.Add(
            new SweepRecord
            {
                TenantId = data.TenantId,
                ReceiverId = sensor,
                StreamId = pulse,
                Resolution = MeasurementGranularity.Daily,
                CompletedOn = first,
                IntegratedTicks = 10,
            });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = SignalReportValidation.Snapshot(data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any));

        // Act
        var actual = await SignalReportBuilder
            .Build(context, request)
            .ToArrayAsync(CancellationToken.None);

        var reference = await SignalReportReference
            .Build(context, request)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(Normalize(actual), Normalize(reference));
        Assert.All(events, value => Assert.True(value.Sequence > 0));
        Assert.Equal(events.Length, events.Select(value => value.Sequence).Distinct().Count());
        foreach (var anchor in request.AnchorIds.Take(3))
        {
            Assert.Contains(reference, row => row.AnchorId == anchor && row.ChannelId == pulse
                && row is { Source: CapturePort.Pulse, Value: 90 });
        }

        Assert.Contains(reference, row => row.AnchorId == sensor && row.ChannelId == pulse
            && row is { Source: CapturePort.Pulse, Value: 50 });
        Assert.Contains(reference, row => row.AnchorId == SignalReportDataset.Id(1) && row.ChannelId == sweep
            && row is { Source: CapturePort.Pulse, Value: 150 });
        Assert.Contains(reference, row => row.AnchorId == SignalReportDataset.Id(1) && row.ChannelId == pulse
            && row is { Source: CapturePort.Sweep, Value: 150 });
        Assert.Contains(reference, row => row.AnchorId == SignalReportDataset.Id(1) && row.ChannelId == pulse
            && row is { Source: CapturePort.Burst, Value: 210 });
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Creates only a caller-owned isolated SQLite connection for functional reference assertions.</summary>
    private static SignalLabContext CreateContext() => new(
        new DbContextOptionsBuilder().UseSqlite(
                "Data Source=:memory:;Pooling=False",
                provider => provider.UseNestedSets())
            .Options);

    /// <summary>Compares scalar groups independently of record materialization and SQL ordering.</summary>
    private static (Guid Anchor, Guid Channel, CapturePort Source, double Value)[] Normalize(
        IEnumerable<SignalReportTotal> rows
    ) => rows
        .OrderBy(row => row.AnchorId)
        .ThenBy(row => row.ChannelId)
        .ThenBy(row => row.Source)
        .Select(row => (row.AnchorId, row.ChannelId, row.Source, row.Value))
        .ToArray();
}
