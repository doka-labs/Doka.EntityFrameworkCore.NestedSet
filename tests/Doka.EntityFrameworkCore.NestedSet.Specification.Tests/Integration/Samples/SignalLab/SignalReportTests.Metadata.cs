using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>
    /// The nearest scenario selects by classification before checking values, so a failed nearby owner has no fallback.
    /// </summary>
    [Theory]
    [InlineData(false, "Receiver", true)]
    [InlineData(true, "Receiver", false)]
    [InlineData(true, "Relay", true)]
    public async Task ReadAsync_NearestReport_DoesNotFallbackToAFartherMatchingOwner(
        bool nearest,
        string moduleClass,
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
            8,
            true,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        var request = data.Request(
                nearest
                    ? TagOwnerMode.Nearest
                    : TagOwnerMode.Any,
                moduleClass,
                false) with
            {
                AnchorIds = [SignalReportDataset.Id(1)],
                ChannelIds = [SignalReportDataset.PulseChannel],
                Sources = [CapturePort.Pulse],
            };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        var cell = Assert.Single(cells);
        Assert.Equal(expected, cell.HasData);
        Assert.Equal(
            expected
                ? 10
                : 0,
            cell.Value);

        AssertReportBoundary(context, probe);
    }

    /// <summary>Sensor fields on different ancestors cannot combine into a synthetic matching owner.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ReadAsync_MetadataReport_RequiresAllFieldsOnTheSameOwner(
        bool matchingOwner,
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
            8,
            true,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        if (matchingOwner)
        {
            await context
                .TagValues
                .Where(value => value.TenantId == data.TenantId
                    && value.NodeId == SignalReportDataset.Id(6)
                    && value.FieldCode == "RackCode")
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        value => value.Value,
                        "R7"),
                    CancellationToken.None);
        }

        var request = SingleAnchor(data) with
        {
            Criteria = data.Request(moduleClass: "Relay")
                .Criteria,
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        var cell = Assert.Single(cells);
        Assert.Equal(expected, cell.HasData);
        Assert.Equal(
            expected
                ? 10
                : 0,
            cell.Value);

        AssertReportBoundary(context, probe);
    }
}
