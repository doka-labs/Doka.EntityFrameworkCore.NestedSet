namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>Exposes asynchronous reader boundaries independently from EF or database buffering.</summary>
    private static ProbeReader<T> ReadRows<T>(
        IReadOnlyList<T> rows,
        ReaderProbe probe,
        CancellationToken cancellationToken
    ) => new ProbeReader<T>(rows, probe, cancellationToken);

    /// <summary>Supplies one row at a time and keeps reader failure distinct from asynchronous disposal.</summary>
    private sealed class ProbeReader<T>(
        IReadOnlyList<T> rows,
        ReaderProbe probe,
        CancellationToken cancellationToken
    ) : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        private int _index;

        /// <summary>Gets the last supplied projection.</summary>
        public T Current => rows[_index - 1];

        /// <summary>Reads all supplied rows before applying the selected EOF failure or cancellation.</summary>
        public async ValueTask<bool> MoveNextAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_index < rows.Count)
            {
                _index++;
                probe.ReadCount++;

                return true;
            }

            if (probe.CancelAfterRows is not null)
            {
                await probe.CancelAfterRows.CancelAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (probe.ReadFailure is not null)
            {
                throw probe.ReadFailure;
            }

            probe.EndReached = true;

            return false;
        }

        /// <summary>Checks the caller's cancellation before exposing the sole reader.</summary>
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            probe.EnumerationToken = token;

            return this;
        }

        /// <summary>Records asynchronous disposal independently from successful EOF.</summary>
        public ValueTask DisposeAsync()
        {
            probe.Disposed = true;

            return probe.DisposeFailure is { } error ? ValueTask.FromException(error) : ValueTask.CompletedTask;
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
