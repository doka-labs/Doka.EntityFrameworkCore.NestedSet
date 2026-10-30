using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Equal observations with separate event identities remain separate contributions in every input format.</summary>
    /// <param name="portName">The physical acquisition format normalized by its own SQL projection.</param>
    [Theory]
    [InlineData("Pulse")]
    [InlineData("Sweep")]
    [InlineData("Burst")]
    public async Task DistinctEqualEventsRetainTheirPhysicalMultiplicity(string portName)
    {
        // Arrange
        var port = Enum.Parse<CapturePort>(portName);
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var first = AddObservation(context, data, port, SignalReportDataset.PulseChannel,
            MeasurementGranularity.Daily, 7);
        var second = AddObservation(context, data, port, SignalReportDataset.PulseChannel,
            MeasurementGranularity.Daily, 7);

        await context.SaveChangesAsync(CancellationToken.None);
        var firstIdentity = EventSequence(first);
        var secondIdentity = EventSequence(second);
        var request = ObservationRequest(data, port);
        context.ChangeTracker.Clear();
        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.True(firstIdentity > 0);
        Assert.True(secondIdentity > 0);
        Assert.NotEqual(firstIdentity, secondIdentity);
        Assert.Equal(10 * ((int)port + 1) + 14, Assert.Single(cells).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>Each physical projection preserves its own channel and resolution before common filtering.</summary>
    /// <param name="portName">The input whose differently named physical columns must retain their meaning.</param>
    [Theory]
    [InlineData("Pulse")]
    [InlineData("Sweep")]
    [InlineData("Burst")]
    public async Task HeterogeneousInputsCannotBypassChannelOrResolutionFilters(string portName)
    {
        // Arrange
        var port = Enum.Parse<CapturePort>(portName);
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        AddObservation(context, data, port, SignalReportDataset.SweepChannel, MeasurementGranularity.Daily, 700);
        AddObservation(context, data, port, SignalReportDataset.PulseChannel, MeasurementGranularity.Monthly, 900);
        await context.SaveChangesAsync(CancellationToken.None);
        var request = ObservationRequest(data, port);
        context.ChangeTracker.Clear();
        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(10 * ((int)port + 1), Assert.Single(cells).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>Targets one sensor, channel, acquisition format and day without changing tree membership.</summary>
    private static SignalReportRequest ObservationRequest(SignalReportDataset data, CapturePort port)
        => data.Request() with
        {
            AnchorIds = [SignalReportDataset.Id(4)],
            ChannelIds = [SignalReportDataset.PulseChannel],
            Sources = [port],
            UntilExclusive = SignalReportDataset.FirstDay.AddDays(1),
            Criteria = [],
        };

    /// <summary>Reads the native generated identity from each actual physical format without a common DTO.</summary>
    private static long EventSequence(object observation) => observation switch
    {
        PulseRecord pulse => pulse.Sequence,
        SweepRecord sweep => sweep.Sequence,
        BurstRecord burst => burst.Sequence,
        _ => throw new ArgumentException("Unknown acquisition input.", nameof(observation)),
    };

    /// <summary>Creates genuinely different wire-format rows that normalize to the same observation grain.</summary>
    private static object AddObservation(
        SignalLabContext context,
        SignalReportDataset data,
        CapturePort port,
        Guid channel,
        MeasurementGranularity resolution,
        double value
    )
    {
        // WHY: Use each format's actual physical fields; a common test DTO would miss a crossed SQL projection.
        object observation = port switch
        {
            CapturePort.Pulse => new PulseRecord
            {
                TenantId = data.TenantId,
                NodeId = SignalReportDataset.Id(4),
                ChannelId = channel,
                Granularity = resolution,
                Day = SignalReportDataset.FirstDay,
                PulseCount = value,
            },
            CapturePort.Sweep => new SweepRecord
            {
                TenantId = data.TenantId,
                ReceiverId = SignalReportDataset.Id(4),
                StreamId = channel,
                Resolution = resolution,
                CompletedOn = SignalReportDataset.FirstDay,
                IntegratedTicks = value,
            },
            CapturePort.Burst => new BurstRecord
            {
                TenantId = data.TenantId,
                DeviceId = SignalReportDataset.Id(4),
                SignalId = channel,
                BucketSize = resolution,
                CapturedOn = SignalReportDataset.FirstDay,
                Magnitude = value,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(port)),
        };

        // WHY: Tracking these identity-generated fixture rows performs no I/O; their generated values
        // are read back by the caller's awaited SaveChangesAsync rather than an asynchronous key generator.
        // ReSharper disable once MethodHasAsyncOverload
        context.Add(observation);

        return observation;
    }
}
