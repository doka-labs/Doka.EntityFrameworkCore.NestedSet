namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies completion and failure propagation at the framework-required iteration seam.</summary>
public sealed class BenchmarkLifecycleTests
{
    /// <summary>Preparation is complete before its synchronous framework hook returns.</summary>
    [Fact]
    public void CompletionWaitsForAsynchronousAction()
    {
        // Arrange
        var completed = false;
        var preparation = Task.Run(
            async () =>
            {
                await Task.Yield();
                completed = true;
            },
            CancellationToken.None);

        // Act
        BenchmarkLifecycle.Complete(preparation);

        // Assert
        Assert.True(completed);
        Assert.True(preparation.IsCompletedSuccessfully);
    }

    /// <summary>A failed asynchronous hook retains its exact exception instead of wrapping it.</summary>
    [Fact]
    public void CompletionPreservesOriginalException()
    {
        // Arrange
        var original = new InvalidOperationException("Injected iteration failure.");
        var preparation = Task.FromException(original);

        // Act
        var error = Record.Exception(() => BenchmarkLifecycle.Complete(preparation));

        // Assert
        Assert.Same(original, error);
    }

    /// <summary>Cancellation remains attributable to its original caller token.</summary>
    [Fact]
    public void CompletionPreservesCallerCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var preparation = Task.FromCanceled(cancellation.Token);

        // Act
        var error = Record.Exception(() => BenchmarkLifecycle.Complete(preparation));

        // Assert
        var canceled = Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }

    /// <summary>Canceled fixture acquisition never opens a database or transfers resource ownership.</summary>
    [Fact]
    public void CanceledFixtureAcquisitionPreservesToken()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var scenario = new BenchmarkScenario(20, BenchmarkShape.Wide, 1, 0);

        // Act
        var error = Record.Exception(() => BenchmarkFixture.Create(
            scenario,
            false,
            new BenchmarkRunOptions(),
            "Data Source=:memory:;Pooling=False",
            cancellation.Token));

        // Assert
        var canceled = Assert.IsType<OperationCanceledException>(error);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }
}
