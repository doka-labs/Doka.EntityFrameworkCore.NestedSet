namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks the duration metadata that real collectors receive when instruments are published.</summary>
public sealed class HistogramAdviceTests
{
    /// <summary>Both duration histograms recommend subsecond buckets while retaining seconds as the unit.</summary>
    /// <param name="name">The duration instrument whose public metadata is checked.</param>
    [Theory]
    [InlineData("nestedset.operation.duration")]
    [InlineData("nestedset.lock.wait.duration")]
    public async Task DurationHistogramPublishesSecondsBasedBucketAdvice(
        string name
    )
    {
        // Arrange
        var instruments = new System.Collections.Concurrent.ConcurrentDictionary<string, Histogram<double>>(
            StringComparer.Ordinal);

        double[] expected =
            [0.0001, 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60];

        await using var context = new DbContext(
            new DbContextOptionsBuilder()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .Options);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == NestedSetDiagnostics.MeterName
                && instrument is Histogram<double> histogram)
            {
                instruments.TryAdd(instrument.Name, histogram);
            }
        };

        // Act
        listener.Start();

        // WHY: Publication must be observed whether another test already initialized the shared instrumentation.
        await Diagnostics.NestedSetTelemetry.ExecuteAsync(
            context,
            "validate",
            _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, instruments.Count);
        Assert.True(instruments.TryGetValue(name, out var actual));
        Assert.Equal("s", actual.Unit);
        Assert.NotNull(actual.Advice);

        var boundaries = actual.Advice.HistogramBucketBoundaries;
        Assert.NotNull(boundaries);
        Assert.Equal(expected, boundaries);
        Assert.All(boundaries, boundary => Assert.True(double.IsFinite(boundary) && boundary > 0));

        for (var index = 1; index < boundaries.Count; index++)
        {
            Assert.True(boundaries[index - 1] < boundaries[index]);
        }
    }
}
