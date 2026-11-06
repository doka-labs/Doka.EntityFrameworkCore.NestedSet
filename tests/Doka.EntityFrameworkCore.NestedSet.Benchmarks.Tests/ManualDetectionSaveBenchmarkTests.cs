namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Checks actual save effects and manual-detection ownership outside observational measurement.</summary>
public sealed class ManualDetectionSaveBenchmarkTests
{
    /// <summary>Gets each measured operation across the independently generated fixture shapes.</summary>
    public static TheoryData<string, BenchmarkShape> SaveCases
    {
        get
        {
            var result = new TheoryData<string, BenchmarkShape>();
            string[] operations =
            [
                nameof(ManualDetectionSaveBenchmarks.MarkedOrder),
                nameof(ManualDetectionSaveBenchmarks.MarkedParent),
                nameof(ManualDetectionInsertBenchmarks.SingleChild),
                nameof(ManualDetectionInsertBenchmarks.Subtree),
                nameof(ManualDetectionInsertBenchmarks.Forest),
            ];

            foreach (var operation in operations)
            {
                foreach (var shape in Enum.GetValues<BenchmarkShape>())
                {
                    result.Add(operation, shape);
                }
            }

            return result;
        }
    }

    /// <summary>Every measured path persists real work and restores the caller's disabled setting.</summary>
    /// <param name="operation">The public measured save operation.</param>
    /// <param name="shape">The topology prepared before measurement begins.</param>
    [Theory]
    [MemberData(nameof(SaveCases))]
    public async Task MeasuredSavePersistsExpectedWorkWithDetectionDisabled(
        string operation,
        BenchmarkShape shape
    )
    {
        // Arrange
        var benchmark = CreateBenchmark(
            shape,
            operation is nameof(ManualDetectionSaveBenchmarks.MarkedOrder)
                or nameof(ManualDetectionSaveBenchmarks.MarkedParent));

        try
        {
            benchmark.Initialize();
            await benchmark.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken);
            Func<Task> save = (benchmark, operation) switch
            {
                (ManualDetectionSaveBenchmarks saveBenchmark, nameof(ManualDetectionSaveBenchmarks.MarkedOrder)) =>
                    saveBenchmark.MarkedOrder,
                (ManualDetectionSaveBenchmarks saveBenchmark, nameof(ManualDetectionSaveBenchmarks.MarkedParent)) =>
                    saveBenchmark.MarkedParent,
                (ManualDetectionInsertBenchmarks insert, nameof(ManualDetectionInsertBenchmarks.SingleChild)) =>
                    insert.SingleChild,
                (ManualDetectionInsertBenchmarks insert, nameof(ManualDetectionInsertBenchmarks.Subtree)) =>
                    insert.Subtree,
                (ManualDetectionInsertBenchmarks insert, nameof(ManualDetectionInsertBenchmarks.Forest)) =>
                    insert.Forest,
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };

            // Act
            await save();

            // Assert
            Assert.False(benchmark.Fixture.Context.ChangeTracker.AutoDetectChangesEnabled);
            await benchmark.VerifyAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>An unexecuted save must never produce a valid-looking timing observation.</summary>
    /// <param name="coordinated">Whether the measured family coordinates tracked changes or stages insertions.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerificationRejectsUnexecutedSave(
        bool coordinated
    )
    {
        // Arrange
        var benchmark = CreateBenchmark(BenchmarkShape.Wide, coordinated);

        try
        {
            benchmark.Initialize();
            await benchmark.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Act
            var error = await Record.ExceptionAsync(() => benchmark.VerifyAsync(TestContext.Current.CancellationToken));

            // Assert
            Assert.IsType<InvalidOperationException>(error);
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>Uses unchanged application rows so the compared save work stays identical between versions.</summary>
    /// <param name="shape">The deterministic hierarchy generated for this case.</param>
    /// <param name="coordinated">Whether existing hierarchy entries are preloaded for coordinated saves.</param>
    /// <returns>The uninitialized benchmark whose full public lifecycle the test owns.</returns>
    private static DatabaseBenchmark CreateBenchmark(
        BenchmarkShape shape,
        bool coordinated
    )
    {
        DatabaseBenchmark benchmark = coordinated
            ? new ManualDetectionSaveBenchmarks()
            : new ManualDetectionInsertBenchmarks();

        benchmark.Nodes = 32;
        benchmark.Shape = shape;
        benchmark.Trees = 2;
        benchmark.Tracked = 128;

        return benchmark;
    }
}
