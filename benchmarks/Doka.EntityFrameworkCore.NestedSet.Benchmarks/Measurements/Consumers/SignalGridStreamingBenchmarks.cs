using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures the original dense streaming and late-failing large sparse materialization workloads.</summary>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class SignalGridStreamingBenchmarks : IAsyncDisposable
{
    private ValidatedSignalReport? _denseRequest;
    private ValidatedSignalReport? _sparseRequest;
    private SignalGridMeasurementRows? _denseRows;
    private SignalGridMeasurementRows? _sparseRows;
    private InvalidOperationException? _failure;
    private SignalReportTotal[]? _sparseTotals;
    private Task<SignalReportCell[]>? _denseResult;
    private Task<Exception?>? _sparseResult;

    /// <summary>Prepares all original scalar rows outside timed materialization.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        _denseRequest = SignalGridMeasurementData.Request(64, 32);
        var totals = _denseRequest.AnchorIds.SelectMany(anchor =>
                _denseRequest.ChannelIds.SelectMany(channel =>
                    _denseRequest.Sources.Select(source => SignalGridMeasurementData.Total(
                        anchor, channel, source, source == CapturePort.Pulse ? 0 : 13))))
            .ToArray();

        _denseRows = new SignalGridMeasurementRows(totals);
        _sparseRequest = SignalGridMeasurementData.Request(512, 512);
        _sparseTotals =
        [
            SignalGridMeasurementData.Total(
                _sparseRequest.AnchorIds[0], _sparseRequest.ChannelIds[0], CapturePort.Pulse, 0),
            SignalGridMeasurementData.Total(
                _sparseRequest.AnchorIds[^1], _sparseRequest.ChannelIds[^1], CapturePort.Burst, 7),
        ];
    }

    /// <summary>Restores reader positions outside each single measured operation.</summary>
    [IterationSetup]
    public void Prepare()
    {
        (_denseRows ?? throw new InvalidOperationException("Prepare dense inputs first.")).Reset();
        var sparse = _sparseTotals ?? throw new InvalidOperationException("Prepare sparse inputs first.");

        // WHY: Each original faulted read owns its exception/Task state. Reusing a thrown exception across
        // observations would measure accumulated diagnostic state rather than the same prepared failure path.
        _failure = new InvalidOperationException("late sparse reader failure");
        _sparseRows = new SignalGridMeasurementRows(sparse, _failure);
        _denseResult = null;
        _sparseResult = null;
    }

    /// <summary>Streams the complete original 6144-cell dense grain through the actual async materializer.</summary>
    /// <returns>The actual sample task without an additional public result wrapper.</returns>
    [Benchmark]
    public Task Dense()
    {
        _denseResult = SignalReportValidation.CreateGridAsync(_denseRequest!, _denseRows!, CancellationToken.None);

        return _denseResult;
    }

    /// <summary>Consumes the two sparse rows and retains the expected original late failure.</summary>
    /// <returns>The expected failure observation; unexpected behavior is rejected during cleanup.</returns>
    [Benchmark]
    public Task<Exception?> SparseFailure()
    {
        _sparseResult = CaptureSparseFailureAsync(CancellationToken.None);

        return _sparseResult;
    }

    /// <summary>Checks full result or original failure and reader ownership after each measured operation.</summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync());

    /// <summary>Checks asynchronous result ownership after the framework's synchronous iteration seam.</summary>
    /// <returns>Completion after exact result and reader-ownership validation.</returns>
    private async Task VerifyAsync()
    {
        try
        {
            await VerifyResultsAsync();
        }
        finally
        {
            // WHY: Verify consumer disposal before fallback cleanup, including a failed ownership oracle.
            await DisposeReadersAsync();
        }
    }

    /// <summary>Releases owned readers even when setup or the measured operation did not complete.</summary>
    /// <returns>Completion after both prepared reader sources are released.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeReadersAsync();
        _denseRows = null;
        _sparseRows = null;
        _denseResult = null;
        _sparseResult = null;
        _denseRequest = null;
        _sparseRequest = null;
        _sparseTotals = null;
        _failure = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>Performs protected cleanup without allowing caller cancellation to retain reader state.</summary>
    private async ValueTask DisposeReadersAsync()
    {
        if (_denseRows is not null)
        {
            await _denseRows.DisposeAsync();
        }

        if (_sparseRows is not null)
        {
            await _sparseRows.DisposeAsync();
        }
    }

    /// <summary>Rejects missing results and changed consumer reader ownership before fallback cleanup.</summary>
    private async Task VerifyResultsAsync()
    {
        if (_denseResult is not null)
        {
            var cells = await _denseResult;
            // WHY: These fixture sums are exactly representable integers, not rounded measurement values.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            var zeroMatches = cells.Length == 6144 && cells[1].Value == 0;

            // WHY: The final fixed fixture sum uses the same exact-integer contract as the explicit zero.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            var totalMatches = cells.Length == 6144 && cells[^1].Value == 13;

            BenchmarkFixture.Require(
                cells.Length == 6144 && cells.All(cell => cell.HasData)
                    && zeroMatches && totalMatches
                    && _denseRows!.Disposed && _denseRows.RowsRead == 6144,
                "Dense streaming changed values, grain or reader ownership.");

            return;
        }

        var error = _sparseResult is null ? null : await _sparseResult;
        BenchmarkFixture.Require(
            ReferenceEquals(error, _failure) && _sparseRows!.Disposed && _sparseRows.RowsRead == 2,
            "Sparse streaming changed the original failure or failed to dispose its reader.");
    }

    /// <summary>Observes the expected fault without reporting an intentional workload as an execution error.</summary>
    private async Task<Exception?> CaptureSparseFailureAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            _ = await SignalReportValidation.CreateGridAsync(_sparseRequest!, _sparseRows!, cancellationToken);

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
