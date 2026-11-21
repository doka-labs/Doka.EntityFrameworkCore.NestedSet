using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>A late failure in a large sparse grain preserves the original error and disposes the reader.</summary>
    [Fact]
    public async Task CreateGridAsync_SparseReaderFailure_PreservesFailureAndDisposesReader()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = Enumerable
                .Range(1, 512)
                .Select(SignalReportDataset.Id)
                .ToArray(),
            ChannelIds = Enumerable
                .Range(101, 512)
                .Select(SignalReportDataset.Id)
                .ToArray(),
            Sources = [CapturePort.Pulse, CapturePort.Sweep, CapturePort.Burst],
        };

        SignalReportTotal[] totals =
        [
            Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 0),
            Total(snapshot.AnchorIds[^1], snapshot.ChannelIds[^1], CapturePort.Burst, 7),
        ];

        var failure = new InvalidOperationException("late sparse reader failure");
        var rows = new SparseFailureReader(totals, failure);

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.CreateGridAsync(
            snapshot,
            rows,
            CancellationToken.None));


        // Assert
        Assert.Same(failure, error);
        Assert.True(rows.Disposed);
    }

    /// <summary>
    /// Supplies two completed reads and a completed fault, without introducing scheduler allocations.
    /// </summary>
    private sealed class SparseFailureReader : IAsyncEnumerable<SignalReportTotal>, IAsyncEnumerator<SignalReportTotal>
    {
        private readonly SignalReportTotal[] _rows;

        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public SparseFailureReader(
            SignalReportTotal[] rows,
            Exception failure
        )
        {
            _rows = rows;
            _failure = Task.FromException<bool>(failure);
        }

        private readonly Task<bool> _failure;
        private int _index = -1;

        /// <summary>Gets whether the failed reader was asynchronously disposed.</summary>
        internal bool Disposed { get; private set; }

        /// <summary>Exposes the sole prepared enumerator after checking the supplied cancellation token.</summary>
        public IAsyncEnumerator<SignalReportTotal> GetAsyncEnumerator(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return this;
        }

        /// <summary>Gets the latest sparse scalar projection.</summary>
        public SignalReportTotal Current => _rows[_index];

        /// <summary>Returns completed values or the prepared late failure.</summary>
        public ValueTask<bool> MoveNextAsync()
        {
            _index++;

            return _index < _rows.Length
                ? ValueTask.FromResult(true)
                : new ValueTask<bool>(_failure);
        }

        /// <summary>Records disposal without scheduling additional work.</summary>
        public ValueTask DisposeAsync()
        {
            Disposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
