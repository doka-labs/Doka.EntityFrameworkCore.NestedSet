using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using BenchmarkRunMode = BenchmarkDotNet.Jobs.RunMode;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Qualifies the pure two-reader materialization fixture without running performance measurements.</summary>
public sealed class SignalMaterializationBenchmarkTests
{
    /// <summary>Gets every selected grain, density, first-read schedule, and incoming source order.</summary>
    public static TheoryData<int, SignalMaterializationDensity, bool, bool> Conditions
    {
        get
        {
            var result = new TheoryData<int, SignalMaterializationDensity, bool, bool>();
            foreach (var cells in new[] { 24, 6144 })
            {
                foreach (var density in Enum.GetValues<SignalMaterializationDensity>())
                {
                    foreach (var suspend in new[] { false, true })
                    {
                        foreach (var interleaved in new[] { false, true })
                        {
                            result.Add(cells, density, suspend, interleaved);
                        }
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Repeated operations preserve all cells and their reader lifecycle for every prepared condition.
    /// </summary>
    /// <param name="cells">The complete selected grain.</param>
    /// <param name="density">The number of measured scalar groups.</param>
    /// <param name="suspend">Whether each reader yields on its first read.</param>
    /// <param name="interleaved">Whether totals arrive grouped by source rather than reversed selection order.</param>
    [Theory]
    [MemberData(nameof(Conditions))]
    public async Task EveryConditionPreservesIndependentCellsAndReaderCounts(
        int cells,
        SignalMaterializationDensity density,
        bool suspend,
        bool interleaved
    )
    {
        // Arrange
        var benchmark = new SignalMaterializationBenchmarks
        {
            Cells = cells,
            Density = density,
            SuspendFirstRead = suspend,
            InterleavedSources = interleaved,
        };

        await benchmark.SetupAsync();
        var expected = Expected(cells, density);

        // Act
        var earlier = (SignalReportCell[])await benchmark.MaterializeAsync();
        var actual = (SignalReportCell[])await benchmark.MaterializeAsync();

        // Assert
        Assert.Equal(expected, earlier);
        Assert.Equal(expected, actual);
        Assert.Same(actual, benchmark.Result);
        Assert.NotSame(earlier, actual);
        Assert.Equal(density != SignalMaterializationDensity.Empty, actual[0].HasData);
        Assert.Equal(0, actual[0].Value);
        Assert.Equal(density == SignalMaterializationDensity.Dense, actual[^1].HasData);
        Assert.Null(await Record.ExceptionAsync(benchmark.VerifyAsync));
    }

    /// <summary>Unknown grains and densities fail before any usable reader fixture remains.</summary>
    /// <param name="cells">The unsupported grain or a valid grain paired with an invalid density.</param>
    /// <param name="density">The requested valid or deliberately unknown density.</param>
    [Theory]
    [InlineData(-1, SignalMaterializationDensity.Empty)]
    [InlineData(0, SignalMaterializationDensity.Dense)]
    [InlineData(25, SignalMaterializationDensity.Sparse)]
    [InlineData(24, (SignalMaterializationDensity)99)]
    public async Task InvalidPreparationIsRejected(
        int cells,
        SignalMaterializationDensity density
    )
    {
        // Arrange
        var benchmark = new SignalMaterializationBenchmarks { Cells = cells, Density = density };

        // Act
        var error = await Record.ExceptionAsync(benchmark.SetupAsync);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(error);
        Assert.Throws<InvalidOperationException>(() => benchmark.Result);
        Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(benchmark.MaterializeAsync));
    }

    /// <summary>Neither measured execution nor cleanup can silently succeed before global preparation.</summary>
    /// <param name="verify">Whether final verification or measured execution is called too early.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UninitializedFixtureIsRejected(bool verify)
    {
        // Arrange
        var benchmark = new SignalMaterializationBenchmarks();

        // Act
        var error = await Record.ExceptionAsync(verify ? benchmark.VerifyAsync : benchmark.MaterializeAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Throws<InvalidOperationException>(() => benchmark.Result);
    }

    /// <summary>The untimed setup control does not count as a measured operation for global cleanup.</summary>
    [Fact]
    public async Task CleanupRejectsSetupWithoutAnExecutedOperation()
    {
        // Arrange
        var benchmark = new SignalMaterializationBenchmarks { Cells = 24 };
        await benchmark.SetupAsync();

        // Act
        var error = await Record.ExceptionAsync(benchmark.VerifyAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Throws<InvalidOperationException>(() => benchmark.Result);
    }

    /// <summary>
    /// Failed reconfiguration cannot expose an older successfully prepared result under new parameters.
    /// </summary>
    [Fact]
    public async Task FailedReconfigurationInvalidatesThePreviousFixture()
    {
        // Arrange
        var benchmark = new SignalMaterializationBenchmarks { Cells = 24 };
        await benchmark.SetupAsync();
        await benchmark.MaterializeAsync();
        benchmark.Cells = 25;

        // Act
        var error = await Record.ExceptionAsync(benchmark.SetupAsync);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(error);
        Assert.Throws<InvalidOperationException>(() => benchmark.Result);
        Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(benchmark.MaterializeAsync));
    }

    /// <summary>
    /// Cleanup rejects changed identities, missing flags, values, or order in the last returned array.
    /// </summary>
    /// <param name="change">The caller-visible corruption applied to the actual returned array.</param>
    [Theory]
    [InlineData("identity")]
    [InlineData("missing")]
    [InlineData("value")]
    [InlineData("order")]
    public async Task CleanupRejectsAlteredResult(string change)
    {
        // Arrange
        var benchmark = new SignalMaterializationBenchmarks { Cells = 24, Density = SignalMaterializationDensity.Dense };
        await benchmark.SetupAsync();
        var result = (SignalReportCell[])await benchmark.MaterializeAsync();
        switch (change)
        {
            case "identity":
                result[0] = result[0] with { ChannelId = Guid.Empty };
                break;
            case "missing":
                result[0] = result[0] with { HasData = false };
                break;
            case "value":
                result[0] = result[0] with { Value = -1 };
                break;
            default:
                (result[0], result[1]) = (result[1], result[0]);
                break;
        }

        // Act
        var error = await Record.ExceptionAsync(benchmark.VerifyAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>The native benchmark discovers all conditions with adaptive throughput and both diagnostics.</summary>
    [Fact]
    public void NativeDiscoveryPreservesTwentyFourAdaptiveConditions()
    {
        // Arrange
        var configuration = BenchmarkConfiguration.Create(new BenchmarkRunOptions());
        using var discovery = BenchmarkConverter.TypeToBenchmarks(typeof(SignalMaterializationBenchmarks), configuration);

        // Act
        var cases = discovery.BenchmarksCases;

        // Assert
        Assert.Equal(24, cases.Length);
        Assert.False(typeof(SignalMaterializationBenchmarks).IsSealed);
        Assert.Contains(discovery.Config.GetDiagnosers(), diagnoser => diagnoser is MemoryDiagnoser);
        Assert.Contains(discovery.Config.GetDiagnosers(), diagnoser => diagnoser is ThreadingDiagnoser);
        Assert.All(cases, benchmark =>
        {
            Assert.Equal(nameof(SignalMaterializationBenchmarks.MaterializeAsync),
                benchmark.Descriptor.WorkloadMethod.Name);
            Assert.Equal(nameof(SignalMaterializationBenchmarks.SetupAsync), benchmark.Descriptor.GlobalSetupMethod?.Name);
            Assert.Equal(nameof(SignalMaterializationBenchmarks.VerifyAsync),
                benchmark.Descriptor.GlobalCleanupMethod?.Name);
            Assert.Null(benchmark.Descriptor.IterationSetupMethod);
            Assert.Null(benchmark.Descriptor.IterationCleanupMethod);
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.InvocationCountCharacteristic));
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.UnrollFactorCharacteristic));
        });
    }

    /// <summary>
    /// Derives output coordinates arithmetically, independently of the fixture's tuple construction.
    /// </summary>
    private static SignalReportCell[] Expected(
        int cells,
        SignalMaterializationDensity density
    )
    {
        var signals = cells == 24 ? 2 : 32;
        var measured = density switch
        {
            SignalMaterializationDensity.Empty => 0,
            SignalMaterializationDensity.Sparse => 2,
            _ => cells,
        };

        CapturePort[] sources = [CapturePort.Sweep, CapturePort.Pulse, CapturePort.Burst];
        var expected = new SignalReportCell[cells];
        for (var position = 0; position < expected.Length; position++)
        {
            var anchor = SignalReportDataset.Id(position / (signals * 3) + 1);
            var signal = SignalReportDataset.Id(101 + position / 3 % signals);
            var exists = position < measured;
            expected[position] = new SignalReportCell(
                anchor,
                signal,
                sources[position % 3],
                exists,
                exists ? position : 0);
        }

        return expected;
    }
}
