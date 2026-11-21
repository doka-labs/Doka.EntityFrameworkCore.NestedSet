using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Malformed requests never fall through to a partial SQL aggregate.</summary>
    [Theory]
    [InlineData("field", 1)]
    [InlineData("type", 1)]
    [InlineData("choice", 1)]
    [InlineData("operator", 0)]
    [InlineData("contradiction", 0)]
    public async Task ReadAsync_InvalidRequest_DoesNotExecuteAnAggregate(
        string shape,
        int expectedCatalogReaders
    )
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var data = await SignalReportDataset.CreateAsync(
            context,
            7,
            false,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        var request = SingleAnchor(data);
        var criterion = new TagCriterion(SignalReportDataset.SensorKind, "RackCode", "R7");
        request = request with
        {
            Criteria = shape switch
            {
                "field" => [criterion with { FieldCode = "x' OR 1=1 --" }],
                "type" => [criterion with { TypeId = SignalReportDataset.StationKind }],
                "choice" => [criterion with { Value = "unknown" }],
                "operator" => [criterion with { Operator = (TagOperator)99 }],
                _ => [criterion, criterion with { Value = "R9" }],
            },
        };

        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportBuilder.ReadAsync(
            context,
            request,
            CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(expectedCatalogReaders, probe.Readers.Count);
        Assert.DoesNotContain(probe.Readers, sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>
    /// Visible anchors and metrics must all belong to the authorized selection; no valid subset is silently reported.
    /// </summary>
    [Theory]
    [InlineData("foreign-tree")]
    [InlineData("hidden")]
    [InlineData("missing-metric")]
    public async Task ReadAsync_Catalog_RejectsAnInvalidSelectionBeforeAnyAggregate(
        string shape
    )
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var data = await SignalReportDataset.CreateAsync(
            context,
            7,
            false,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        var request = SingleAnchor(data);

        if (shape == "hidden")
        {
            await context
                .Nodes
                .Where(node => node.TenantId == data.TenantId && node.Id == SignalReportDataset.Id(4))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        node => node.IsVisible,
                        false),
                    CancellationToken.None);
        }

        request = shape switch
        {
            "foreign-tree" => request with { AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(10)] },
            "hidden" => request with { AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(4)] },
            _ => request with { ChannelIds = [SignalReportDataset.PulseChannel, SignalReportDataset.Id(999)] },
        };

        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportBuilder.ReadAsync(
            context,
            request,
            CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Single(probe.Readers);
        Assert.DoesNotContain("GROUP BY", probe.Readers[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>A valid nearest-owner match never reopens an expired anchor-to-measurement edge.</summary>
    [Fact]
    public async Task ReadAsync_NearestMetadata_DoesNotReopenATemporallyClosedBranch()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var data = await SignalReportDataset.CreateAsync(
            context,
            8,
            true,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        await context
            .LinkAvailability
            .Where(period => period.TenantId == data.TenantId
                && period.ParentId == SignalReportDataset.Id(4)
                && period.ChildId == SignalReportDataset.Id(5))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    period => period.ToDate,
                    SignalReportDataset.FirstDay.AddDays(-1)),
                CancellationToken.None);

        var request = data.Request(TagOwnerMode.Nearest, "Relay", false) with
        {
            AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(6)],
            ChannelIds = [SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Pulse],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.False(
            cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(1))
                .HasData);

        Assert.Equal(
            10,
            cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(6))
                .Value);

        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// Any metadata cannot reopen a hidden anchor path; Nearest additionally requires its own complete owner path.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ReadAsync_VisibilityGaps_RemainDistinctForAnyAndNearest(
        bool nearest,
        bool selfHasData
    )
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var data = await SignalReportDataset.CreateAsync(
            context,
            8,
            true,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        await context
            .Nodes
            .Where(node => node.TenantId == data.TenantId && node.Id == SignalReportDataset.Id(4))
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.IsVisible, false), CancellationToken.None);

        var request = data.Request(
                nearest
                    ? TagOwnerMode.Nearest
                    : TagOwnerMode.Any,
                "Receiver",
                false) with
            {
                AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(6)],
                ChannelIds = [SignalReportDataset.PulseChannel],
                Sources = [CapturePort.Pulse],
            };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.False(
            cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(1))
                .HasData);

        var self = cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(6));
        Assert.Equal(selfHasData, self.HasData);
        Assert.Equal(
            selfHasData
                ? 10
                : 0,
            self.Value);

        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// A classification with no owner yields no qualifying group, rather than choosing a root default.
    /// </summary>
    [Fact]
    public async Task ReadAsync_MissingNearestOwner_DoesNotSupplyARootDefault()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var data = await SignalReportDataset.CreateAsync(
            context,
            8,
            true,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        var request = data.Request(TagOwnerMode.Nearest, "Receiver", false) with
        {
            TreeId = SignalReportDataset.Id(4008),
            AnchorIds = [SignalReportDataset.Id(10)],
            ChannelIds = [SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Pulse],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        var cell = Assert.Single(cells);
        Assert.False(cell.HasData);
        Assert.Equal(0, cell.Value);
        AssertReportBoundary(context, probe);
    }
}
