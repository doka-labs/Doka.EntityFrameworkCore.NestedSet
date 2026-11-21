namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>Exposes asynchronous reader boundaries independently from EF or database buffering.</summary>
    private static ProbeReader<T> ReadRows<T>(
        IReadOnlyList<T> rows,
        ReaderProbe probe,
        CancellationToken cancellationToken
    ) => new(rows, probe, cancellationToken);

    /// <summary>Supplies one row at a time and keeps reader failure distinct from asynchronous disposal.</summary>
    private sealed class ProbeReader<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        private readonly IReadOnlyList<T> _rows;
        private readonly ReaderProbe _probe;
        private readonly CancellationToken _cancellationToken;

        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public ProbeReader(
            IReadOnlyList<T> rows,
            ReaderProbe probe,
            CancellationToken cancellationToken
        )
        {
            _rows = rows;
            _probe = probe;
            _cancellationToken = cancellationToken;
        }

        private int _index;

        /// <summary>Checks the caller's cancellation before exposing the sole reader.</summary>
        public IAsyncEnumerator<T> GetAsyncEnumerator(
            CancellationToken iterationCancellationToken
        )
        {
            iterationCancellationToken.ThrowIfCancellationRequested();
            _probe.EnumerationToken = iterationCancellationToken;

            return this;
        }

        /// <summary>Gets the last supplied projection.</summary>
        public T Current => _rows[_index - 1];

        /// <summary>Reads all supplied rows before applying the selected EOF failure or cancellation.</summary>
        public ValueTask<bool> MoveNextAsync()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<bool>(_cancellationToken);
            }

            if (_index < _rows.Count)
            {
                _index++;
                _probe.ReadCount++;

                // WHY: Prepared rows complete synchronously. An async state machine per row allocates in
                // Debug and would measure this test reader instead of the report's temporary state.
                return ValueTask.FromResult(true);
            }

            return CompleteAsync(_cancellationToken);
        }

        /// <summary>Records asynchronous disposal independently from successful EOF.</summary>
        public ValueTask DisposeAsync()
        {
            _probe.Disposed = true;

            return _probe.DisposeFailure is { } error
                ? ValueTask.FromException(error)
                : ValueTask.CompletedTask;
        }

        /// <summary>Preserves asynchronous EOF cancellation and failure before successful completion.</summary>
        /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
        private async ValueTask<bool> CompleteAsync(
            CancellationToken cancellationToken
        )
        {
            if (_probe.CancelAfterRows is not null)
            {
                await _probe.CancelAfterRows.CancelAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_probe.ReadFailure is not null)
            {
                throw _probe.ReadFailure;
            }

            _probe.EndReached = true;

            return false;
        }
    }

    /// <summary>Records EOF and disposal, with explicit late failures used by rejection precedence tests.</summary>
    private sealed class ReaderProbe
    {
        /// <summary>Gets or sets the number of rows offered to the consumer.</summary>
        internal int ReadCount { get; set; }

        /// <summary>Gets or sets whether the underlying reader successfully reached EOF.</summary>
        internal bool EndReached { get; set; }

        /// <summary>Gets or sets whether asynchronous disposal ran.</summary>
        internal bool Disposed { get; set; }

        /// <summary>Gets or sets the exact token forwarded by the async consumer to its source.</summary>
        internal CancellationToken EnumerationToken { get; set; }

        /// <summary>Gets the error raised after the last supplied row, before successful EOF.</summary>
        internal Exception? ReadFailure { get; init; }

        /// <summary>Gets the error raised during asynchronous reader disposal.</summary>
        internal Exception? DisposeFailure { get; init; }

        /// <summary>Gets the cancellation source stopped after all rows but before EOF.</summary>
        internal CancellationTokenSource? CancelAfterRows { get; init; }
    }
}
