namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Retains the exact capacity adapters and request variants independently of numerical measurements.</summary>
public sealed class CapacityConsumerMeasurementTests
{
    /// <summary>All former view-backed Signal, supervisor, initial-chain and rank constructions remain deferred.</summary>
    /// <param name="engine">The real provider dialect used for adapted key and union-literal mapping.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    public async Task Construct_ActualCapacityAdapters_RetainsAllRequests(BenchmarkEngine engine)
    {
        // Arrange
        var benchmark = new CapacityConsumerConstructionBenchmarks { Engine = engine };
        benchmark.Initialize();

        try
        {
            // Act
            var queries = CapacityConsumerConstructionBenchmarks.Cases.Select(value =>
            {
                benchmark.Case = value;
                benchmark.PrepareRequest();

                return benchmark.Construct();
            }).ToArray();

            // Assert
            Assert.Equal(32, queries.Length);
            Assert.All(queries, query => Assert.NotNull(query));
        }
        finally
        {
            await benchmark.DisposeAsync();
        }
    }

    /// <summary>Unknown capacity profiles fail before any query provider can execute.</summary>
    [Fact]
    public async Task PrepareRequest_UnknownProfile_RejectsContractDrift()
    {
        // Arrange
        var benchmark = new CapacityConsumerConstructionBenchmarks
        {
            Engine = BenchmarkEngine.SqliteMemory,
            Case = new("Unknown", 100_001, 1, 1, false),
        };

        try
        {
            // Act
            var error = Record.Exception(benchmark.Initialize);

            // Assert
            Assert.IsType<InvalidOperationException>(error);
        }
        finally
        {
            await benchmark.DisposeAsync();
        }
    }
}
