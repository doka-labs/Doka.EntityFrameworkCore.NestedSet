namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Verifies evidence and fixture cleanup failures without acquiring database or filesystem resources.
/// </summary>
public sealed class QueryPlanFixtureCleanupTests
{
    /// <summary>Successful teardown writes evidence before cleaning up its owned resources.</summary>
    [Fact]
    public async Task SuccessfulTeardownWritesBeforeCleanup()
    {
        // Arrange
        var calls = new List<string>();

        // Act
        await QueryPlanTestSupport.CompleteFixtureAsync(
            () =>
            {
                calls.Add("write");

                return Task.CompletedTask;
            },
            () =>
            {
                calls.Add("cleanup");

                return Task.CompletedTask;
            });

        // Assert
        Assert.Equal(["write", "cleanup"], calls);
    }

    /// <summary>A lone writer failure retains its identity while cleanup still completes.</summary>
    /// <param name="synchronous">Whether the writer throws before returning its task.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriterFailureStillCleansUpAndRetainsOriginalException(
        bool synchronous
    )
    {
        // Arrange
        var expected = new InvalidOperationException("Evidence failure.");
        var cleaned = false;
        Func<Task> write = synchronous ? () => throw expected : () => Task.FromException(expected);

        // Act
        var actual = await Record.ExceptionAsync(async () => await QueryPlanTestSupport.CompleteFixtureAsync(
            write,
            () =>
            {
                cleaned = true;

                return Task.CompletedTask;
            }));

        // Assert
        Assert.Same(expected, actual);
        Assert.True(cleaned);
    }

    /// <summary>A lone cleanup failure retains its identity after the evidence writer completed.</summary>
    /// <param name="synchronous">Whether cleanup throws before returning its task.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureRetainsOriginalException(
        bool synchronous
    )
    {
        // Arrange
        var expected = new InvalidOperationException("Cleanup failure.");
        var written = false;
        Func<Task> cleanup = synchronous ? () => throw expected : () => Task.FromException(expected);

        // Act
        var actual = await Record.ExceptionAsync(async () => await QueryPlanTestSupport.CompleteFixtureAsync(
            () =>
            {
                written = true;

                return Task.CompletedTask;
            },
            cleanup));

        // Assert
        Assert.Same(expected, actual);
        Assert.True(written);
    }

    /// <summary>Simultaneous writer and cleanup failures remain visible in their original operation order.</summary>
    [Fact]
    public async Task WriterAndCleanupFailuresAreBothPreserved()
    {
        // Arrange
        var writerFailure = new IOException("Evidence failure.");
        var cleanupFailure = new InvalidOperationException("Cleanup failure.");

        // Act
        var actual = await Record.ExceptionAsync(async () => await QueryPlanTestSupport.CompleteFixtureAsync(
            () => Task.FromException(writerFailure),
            () => Task.FromException(cleanupFailure)));

        // Assert
        var aggregate = Assert.IsType<AggregateException>(actual);
        Assert.Collection(aggregate.InnerExceptions,
            error => Assert.Same(writerFailure, error),
            error => Assert.Same(cleanupFailure, error));
    }

    /// <summary>
    /// Every failed database cleanup survives the single exception normally exposed by WhenAll await.
    /// </summary>
    /// <param name="writerFails">Whether evidence writing failed before both cleanup tasks.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllOwnedCleanupFailuresRemainVisible(
        bool writerFails
    )
    {
        // Arrange
        var writerFailure = new IOException("Evidence failure.");
        var firstCleanupFailure = new InvalidOperationException("First database failure.");
        var secondCleanupFailure = new IOException("Second database failure.");

        // Act
        var actual = await Record.ExceptionAsync(async () => await QueryPlanTestSupport.CompleteFixtureAsync(
            () => writerFails ? Task.FromException(writerFailure) : Task.CompletedTask,
            () => Task.WhenAll(Task.FromException(firstCleanupFailure), Task.FromException(secondCleanupFailure))));

        // Assert
        var aggregate = Assert.IsType<AggregateException>(actual);
        Assert.Equal(writerFails ? 3 : 2, aggregate.InnerExceptions.Count);
        Assert.Same(firstCleanupFailure, aggregate.InnerExceptions[^2]);
        Assert.Same(secondCleanupFailure, aggregate.InnerExceptions[^1]);

        if (writerFails)
        {
            Assert.Same(writerFailure, aggregate.InnerExceptions[0]);
        }
    }

    /// <summary>An explicitly thrown aggregate is one original failure rather than a flattened replacement.</summary>
    [Fact]
    public async Task LoneExplicitAggregateRetainsOriginalException()
    {
        // Arrange
        var expected = new AggregateException("Original aggregate.", new IOException("Nested failure."));

        // Act
        var actual = await Record.ExceptionAsync(async () => await QueryPlanTestSupport.CompleteFixtureAsync(
            () => Task.FromException(expected),
            () => Task.CompletedTask));

        // Assert
        Assert.Same(expected, actual);
    }

    /// <summary>Canceled evidence still triggers owned-resource cleanup before cancellation is propagated.</summary>
    [Fact]
    public async Task CanceledWriterStillCleansUp()
    {
        // Arrange
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var cleaned = false;

        // Act
        var actual = await Record.ExceptionAsync(async () => await QueryPlanTestSupport.CompleteFixtureAsync(
            () => Task.FromCanceled(canceled.Token),
            () =>
            {
                cleaned = true;

                return Task.CompletedTask;
            }));

        // Assert
        Assert.Equal(canceled.Token, Assert.IsAssignableFrom<OperationCanceledException>(actual).CancellationToken);
        Assert.True(cleaned);
    }
}
