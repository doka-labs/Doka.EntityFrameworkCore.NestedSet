using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Known case-variant choices still compare case-sensitively in actual SQL on every provider.</summary>
    [Theory]
    [InlineData("Receiver", true)]
    [InlineData("receiver", false)]
    public async Task ReadAsync_FixedMetadataComparison_MatchesOrdinalRequestPolicy(
        string selected,
        bool expected
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
        context.TagChoices.Add(
            new TagChoice
            {
                TenantId = data.TenantId,
                TypeId = SignalReportDataset.ModuleKind,
                FieldCode = "ModuleClass",
                Value = "receiver",
            });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = SingleAnchor(data) with
        {
            Criteria = [new TagCriterion(SignalReportDataset.ModuleKind, "ModuleClass", selected)],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        var cell = Assert.Single(cells);
        Assert.Equal(expected, cell.HasData);
        Assert.Equal(
            expected
                ? 70
                : 0,
            cell.Value);

        AssertReportBoundary(context, probe);
    }

    /// <summary>A separately permitted SQL-like value remains a scalar parameter, never SQL structure.</summary>
    [Fact]
    public async Task ReadAsync_SqlLikeMetadataText_IsParameterizedWithoutExpandingTheResult()
    {
        // Arrange
        const string selected = "x' OR 1=1 --";
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
        await context
            .TagValues
            .Where(value => value.TenantId == data.TenantId
                && value.NodeId == SignalReportDataset.Id(4)
                && value.FieldCode == "RackCode")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value, selected), CancellationToken.None);

        var request = SingleAnchor(data) with
        {
            Criteria = [new TagCriterion(SignalReportDataset.SensorKind, "RackCode", selected)],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(
            30,
            Assert.Single(cells)
                .Value);

        Assert.Contains(selected, probe.ParameterValues);
        Assert.DoesNotContain(selected, probe.Readers[1], StringComparison.Ordinal);
        AssertReportBoundary(context, probe);
    }

    /// <summary>Duplicate request IDs and repeated identical fields do not duplicate contributions.</summary>
    [Fact]
    public async Task ReadAsync_DuplicateCriteriaAndSelections_PreserveTheSameSum()
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
        request = request with
        {
            AnchorIds = [request.AnchorIds[0], request.AnchorIds[0]],
            ChannelIds = [request.ChannelIds[0], request.ChannelIds[0]],
            Sources = [CapturePort.Pulse, CapturePort.Pulse],
            Criteria = [.. request.Criteria, .. request.Criteria],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(
            70,
            Assert.Single(cells)
                .Value);

        AssertReportBoundary(context, probe);
    }
}
