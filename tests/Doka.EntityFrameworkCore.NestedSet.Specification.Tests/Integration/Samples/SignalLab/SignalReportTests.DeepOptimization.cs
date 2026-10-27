using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Shared projections retain each deferred report's independent scalar inputs and rejecting metadata.</summary>
    /// <param name="nearest">Whether classifications select their nearest owner before testing metadata values.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterleavedReportBuildsKeepSelectionsAndMetadataIsolated(bool nearest)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var first = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        var second = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        await context.SweepRecords.Where(value => value.TenantId == second.TenantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IntegratedTicks, value => value.IntegratedTicks * 10),
                CancellationToken.None);

        var mode = nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any;
        var firstRequest = SingleAnchor(first) with
        {
            UntilExclusive = SignalReportDataset.FirstDay.AddDays(1),
            Criteria = first.Request(mode).Criteria,
        };

        var secondRequest = SingleAnchor(second) with
        {
            ChannelIds = [SignalReportDataset.SweepChannel],
            Sources = [CapturePort.Sweep],
            FromInclusive = SignalReportDataset.FirstDay.AddDays(1),
            Criteria = second.Request(mode).Criteria,
        };

        var rejectedRequest = firstRequest with
        {
            Criteria = [new TagCriterion(SignalReportDataset.SensorKind, "RackCode", "R9", mode)],
        };

        var firstSnapshot = SignalReportValidation.Snapshot(firstRequest);
        var secondSnapshot = SignalReportValidation.Snapshot(secondRequest);
        var rejectedSnapshot = SignalReportValidation.Snapshot(rejectedRequest);
        probe.Reset();

        // Act
        var firstQuery = SignalReportBuilder.Build(context, firstSnapshot);
        var secondQuery = SignalReportBuilder.Build(context, secondSnapshot);
        var rejectedQuery = SignalReportBuilder.Build(context, rejectedSnapshot);
        // WHY: Reverse execution exposes shared templates that accidentally retain the latest request's bindings.
        var rejectedTotals = await rejectedQuery.ToArrayAsync(CancellationToken.None);
        var secondTotals = await secondQuery.ToArrayAsync(CancellationToken.None);
        var firstTotals = await firstQuery.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(rejectedTotals);
        var secondTotal = Assert.Single(secondTotals);
        var firstTotal = Assert.Single(firstTotals);
        Assert.Equal(SignalReportDataset.Id(1), firstTotal.AnchorId);
        Assert.Equal(SignalReportDataset.PulseChannel, firstTotal.ChannelId);
        Assert.Equal(CapturePort.Pulse, firstTotal.Source);
        Assert.Equal(50, firstTotal.Value);
        Assert.Equal(SignalReportDataset.Id(1), secondTotal.AnchorId);
        Assert.Equal(SignalReportDataset.SweepChannel, secondTotal.ChannelId);
        Assert.Equal(CapturePort.Sweep, secondTotal.Source);
        Assert.Equal(800, secondTotal.Value);
        Assert.Equal(3, probe.Readers.Count);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Sharing day-specific path evidence never combines equal-valued facts, metrics, sources or anchors.</summary>
    /// <param name="nearest">Whether metadata selects the nearest classification before checking its values.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignalDeepOptimizationPreservesIndependentFactBagsAtSharedDays(bool nearest)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        await context.PulseRecords.Where(value => value.TenantId == data.TenantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.PulseCount, 7), CancellationToken.None);
        await context.SweepRecords.Where(value => value.TenantId == data.TenantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IntegratedTicks, 7), CancellationToken.None);
        await context.BurstRecords.Where(value => value.TenantId == data.TenantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Magnitude, 7), CancellationToken.None);
        var request = data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any) with
        {
            AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(3), SignalReportDataset.Id(4), SignalReportDataset.Id(5)],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(24, cells.Length);
        Assert.All(cells, cell =>
        {
            var expected = cell.AnchorId == SignalReportDataset.Id(1) || cell.AnchorId == SignalReportDataset.Id(3) ? 21 : 14;
            Assert.True(cell.HasData);
            Assert.Equal(expected, cell.Value);
        });
        AssertReportBoundary(context, probe);
    }

    /// <summary>Rejected shared paths cannot leak any physical fact while a lower self-anchor keeps its own facts.</summary>
    /// <param name="failure">The missing structural or temporal evidence above the retained self-anchor.</param>
    [Theory]
    [InlineData("hidden")]
    [InlineData("skip-parent")]
    [InlineData("depth")]
    [InlineData("expired")]
    public async Task SignalDeepOptimizationRejectsBrokenSharedPathsAndPreservesLowerAnchors(string failure)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 6, false, CancellationToken.None, Guid.NewGuid());
        await context.Nodes.Where(node => node.TenantId == data.TenantId && node.Id == SignalReportDataset.Id(5))
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.IsVisible, false), CancellationToken.None);

        switch (failure)
        {
            case "hidden":
                await context.Nodes.Where(node => node.TenantId == data.TenantId && node.Id == SignalReportDataset.Id(3))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.IsVisible, false),
                        CancellationToken.None);
                break;
            case "skip-parent":
                await context.Nodes.Where(node => node.TenantId == data.TenantId && node.Id == SignalReportDataset.Id(4))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        node => EF.Property<Guid?>(node, SignalStorage.ParentId), SignalReportDataset.Id(1)),
                        CancellationToken.None);
                break;
            case "depth":
                await context.Nodes.Where(node => node.TenantId == data.TenantId && node.Id == SignalReportDataset.Id(3))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        node => EF.Property<int>(node, SignalStorage.Depth), 100), CancellationToken.None);
                break;
            default:
                await context.LinkAvailability.Where(period => period.TenantId == data.TenantId
                        && period.ParentId == SignalReportDataset.Id(2) && period.ChildId == SignalReportDataset.Id(3))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.ToDate,
                        SignalReportDataset.FirstDay.AddDays(-1)), CancellationToken.None);
                break;
        }

        var request = data.Request() with
        {
            AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(4)],
            Criteria = [],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(12, cells.Length);
        Assert.All(cells, cell =>
        {
            var retained = cell.AnchorId == SignalReportDataset.Id(4);
            var metric = cell.ChannelId == SignalReportDataset.PulseChannel ? 1 : 2;
            Assert.Equal(retained, cell.HasData);
            Assert.Equal(retained ? 30 * metric * ((int)cell.Source + 1) : 0, cell.Value);
        });
        AssertReportBoundary(context, probe);
    }

    /// <summary>Fact reuse cannot turn a rejected nearest owner into a farther matching owner.</summary>
    /// <param name="nearest">Whether the near rejection must suppress the farther matching classification.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignalDeepOptimizationRetainsNearestRejectionForEveryFact(bool nearest)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 8, true, CancellationToken.None, Guid.NewGuid());
        var request = data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any,
            "Receiver", includeSensor: false);

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(12, cells.Length);
        Assert.All(cells, cell =>
        {
            var metric = cell.ChannelId == SignalReportDataset.PulseChannel ? 1 : 2;
            Assert.Equal(!nearest, cell.HasData);
            Assert.Equal(nearest ? 0 : 10 * metric * ((int)cell.Source + 1), cell.Value);
        });
        AssertReportBoundary(context, probe);
    }

    /// <summary>Shared node/day evidence cannot qualify a fact whose own metric requires another classification.</summary>
    [Fact]
    public async Task SignalDeepOptimizationChecksEachMetricsOwnMeasurementTypeAfterSharedProof()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        await context.SignalChannels.Where(signal => signal.TenantId == data.TenantId
                && signal.Id == SignalReportDataset.SweepChannel)
            .ExecuteUpdateAsync(setters => setters.SetProperty(signal => signal.MeasurementTypeId,
                SignalReportDataset.ModuleKind), CancellationToken.None);
        await context.PulseRecords.Where(value => value.TenantId == data.TenantId
                && value.NodeId == SignalReportDataset.Id(4) && value.ChannelId == SignalReportDataset.SweepChannel
                && value.Day == SignalReportDataset.FirstDay && value.Granularity == MeasurementGranularity.Daily)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.PulseCount, 10), CancellationToken.None);
        var request = data.Request() with
        {
            AnchorIds = [SignalReportDataset.Id(4)],
            Sources = [CapturePort.Pulse],
            UntilExclusive = SignalReportDataset.FirstDay.AddDays(1),
            Criteria = [],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(2, cells.Length);
        var accepted = cells.Single(cell => cell.ChannelId == SignalReportDataset.PulseChannel);
        var rejected = cells.Single(cell => cell.ChannelId == SignalReportDataset.SweepChannel);
        Assert.True(accepted.HasData);
        Assert.Equal(10, accepted.Value);
        Assert.False(rejected.HasData);
        Assert.Equal(0, rejected.Value);
        AssertReportBoundary(context, probe);
    }
}
