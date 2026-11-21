using Microsoft.Win32.SafeHandles;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks child-process ownership independently of numeric heap observations or execution speed.</summary>
public sealed class OwnedChildProcessTests
{
    /// <summary>An expired request rejects before starting an independent process and preserves token identity.</summary>
    [Fact]
    public async Task RunFailureControlAsync_CanceledRequest_DoesNotStartIndependentProcess()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => OwnedChildProbe.RunFailureControlAsync("not-started", stop.Token));

        // Assert
        Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
    }

    /// <summary>Cancellation reaps the live child before its process handle is released.</summary>
    [Fact]
    public async Task RunCancellationControlAsync_CanceledChild_ReleasesOwnedProcessHandle()
    {
        // Arrange
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        SafeProcessHandle? handle = null;

        // Act
        var running = OwnedChildProbe.RunCancellationControlAsync(process => handle = process.SafeHandle, stop.Token);
        await stop.CancelAsync();
        var error = await Record.ExceptionAsync(() => running);

        // Assert
        Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.NotNull(handle);
        Assert.True(handle.IsClosed);
    }

    /// <summary>A child failure preserves its diagnostic while the process and streams finish cleanup.</summary>
    [Fact]
    public async Task RunFailureControlAsync_FailedChild_PreservesOriginalFailure()
    {
        // Arrange
        const string marker = "Intentional owned-child failure.";

        // Act
        var error = await Record.ExceptionAsync(() =>
            OwnedChildProbe.RunFailureControlAsync(marker, TestContext.Current.CancellationToken));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Owned child exited with 7", failure.Message, StringComparison.Ordinal);
        Assert.Contains(marker, failure.Message, StringComparison.Ordinal);
    }
}
