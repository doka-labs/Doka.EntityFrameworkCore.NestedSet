using Microsoft.Win32.SafeHandles;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ManagedHeapObservationTests
{
    /// <summary>Cancellation terminates and reaps an owned live child before the calibration task completes.</summary>
    [Fact]
    public async Task RunIsolatedAsync_CanceledChild_ReleasesOwnedProcessHandle()
    {
        // Arrange
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        SafeProcessHandle? handle = null;

        // Act
        var running = ManagedHeapProbe.RunCancellationControlAsync(process => handle = process.SafeHandle, stop.Token);
        await stop.CancelAsync();
        var error = await Record.ExceptionAsync(() => running);

        // Assert
        Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.NotNull(handle);
        Assert.True(handle.IsClosed);
    }

    /// <summary>A child-side failure preserves its diagnostic while its process and streams finish cleanup.</summary>
    [Fact]
    public async Task RunIsolatedAsync_UnknownChildShape_PreservesOriginalFailure()
    {
        // Arrange
        const string shape = "not-a-calibration-shape";

        // Act
        var error = await Record.ExceptionAsync(() =>
            ManagedHeapProbe.RunIsolatedAsync(shape, TestContext.Current.CancellationToken));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Unknown heap calibration shape", failure.Message, StringComparison.Ordinal);
        Assert.Contains(shape, failure.Message, StringComparison.Ordinal);
    }
}
