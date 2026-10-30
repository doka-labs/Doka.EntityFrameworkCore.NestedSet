using System.Globalization;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>Cancellation is returned through the pending read before exposing any prepared row.</summary>
    [Fact]
    public async Task PreparedReaderReturnsCancellationWithoutThrowingSynchronously()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var probe = new ReaderProbe();
        int[] rows = [1];
        var reader = ReadRows(rows, probe, cancellation.Token);

        // Act
        var pending = reader.MoveNextAsync();
        var error = await Record.ExceptionAsync(async () => await pending);

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.True(pending.IsCanceled);
        Assert.Equal(0, probe.ReadCount);
        Assert.False(probe.EndReached);
    }

    /// <summary>Both async consumers forward their exact token even when the source captured no token.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportReadersForwardTheExactEnumerationToken(
        bool aggregate
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var probe = new ReaderProbe();
        using var cancellation = new CancellationTokenSource();
        Func<Task> read = aggregate
            ? () => SignalReportValidation.CreateGridAsync(snapshot,
                ReadRows(Array.Empty<SignalReportTotal>(), probe, CancellationToken.None), cancellation.Token)
            : () => SignalReportValidation.RequireCatalogAsync(snapshot,
                ReadRows(Catalog(snapshot), probe, CancellationToken.None), cancellation.Token);

        // Act
        await read();

        // Assert
        Assert.Equal(cancellation.Token, probe.EnumerationToken);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
    }

    /// <summary>Both adapters preserve the framework's localized duplicate-key formatting and parameter name.</summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public async Task GridDuplicateErrorsMatchTheFrameworkInEachUiCulture(
        string culture
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var total = Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 7);
        var key = (total.AnchorId, total.ChannelId, total.Source);
        var reference = new Dictionary<(Guid AnchorId, Guid ChannelId, CapturePort Source), byte> { [key] = 0 };
        SignalReportTotal[] rows = [total, total];
        var probe = new ReaderProbe();
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            // Act
            var expected = Record.Exception(() => reference.Add(key, 0));
            var synchronous = Record.Exception(() => SignalReportValidation.CreateGrid(snapshot, rows));
            var asynchronous = await Record.ExceptionAsync(() => SignalReportValidation.CreateGridAsync(snapshot,
                ReadRows(rows, probe, CancellationToken.None), CancellationToken.None));

            // Assert
            var frameworkError = Assert.IsType<ArgumentException>(expected);
            var synchronousError = Assert.IsType<ArgumentException>(synchronous);
            var asynchronousError = Assert.IsType<ArgumentException>(asynchronous);
            Assert.Equal(frameworkError.Message, synchronousError.Message);
            Assert.Equal(frameworkError.Message, asynchronousError.Message);
            Assert.Equal(frameworkError.ParamName, synchronousError.ParamName);
            Assert.Equal(frameworkError.ParamName, asynchronousError.ParamName);
            Assert.True(probe.EndReached);
            Assert.True(probe.Disposed);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}
