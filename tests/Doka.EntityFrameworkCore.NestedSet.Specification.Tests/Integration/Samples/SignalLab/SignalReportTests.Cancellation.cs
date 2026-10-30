using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Cancellation after the catalog prevents aggregate I/O and releases the owned snapshot.</summary>
    [Fact]
    public async Task CancellationAfterCatalogPreventsAggregateAndReleasesSnapshot()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var request = data.Request();
        using var cancellation = new CancellationTokenSource();
        probe.Reset();
        probe.AfterFirstReader = _ => cancellation.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportBuilder.ReadAsync(context, request,
            cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Single(probe.Readers);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }
}
