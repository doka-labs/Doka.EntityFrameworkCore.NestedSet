namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Supplies owned scalar rows with bounded lifecycle counters and an optional first-read suspension.</summary>
/// <typeparam name="T">The actual sample scalar projection.</typeparam>
/// <param name="rows">The stable prepared rows reused by serial benchmark invocations.</param>
/// <param name="suspendFirstRead">Whether each enumeration yields once, including an empty enumeration.</param>
internal sealed class SignalMaterializationRows<T>(
    T[] rows,
    bool suspendFirstRead
) : IAsyncEnumerable<T>
{
    /// <summary>Gets how many complete invocations acquired a reader, including setup validation.</summary>
    internal long Enumerations { get; private set; }

    /// <summary>Gets how many reader disposals completed.</summary>
    internal long Disposals { get; private set; }

    /// <summary>Gets how many rows were offered before reader disposal.</summary>
    internal long RowsRead { get; private set; }

    /// <summary>Gets how many disposed readers reached their successful EOF.</summary>
    internal long EndOfStreams { get; private set; }

    /// <summary>Gets how many first reads deliberately suspended.</summary>
    internal long Suspensions { get; private set; }

    /// <summary>Creates an independent reader without copying the stable scalar projections.</summary>
    /// <param name="cancellationToken">The token forwarded by the actual asynchronous consumer.</param>
    /// <returns>The invocation-owned reader.</returns>
    public IAsyncEnumerator<T> GetAsyncEnumerator(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Enumerations++;

        return new Reader(rows, this, suspendFirstRead);
    }

    /// <summary>Retains only its current position and contributes lifecycle totals at disposal.</summary>
    private sealed class Reader(
        T[] values,
        SignalMaterializationRows<T> owner,
        bool suspend
    ) : IAsyncEnumerator<T>
    {
        private int _index = -1;

        /// <summary>Gets the current prepared scalar projection.</summary>
        public T Current => values[_index];

        /// <summary>Offers one scalar or EOF, optionally suspending exactly the first read.</summary>
        /// <returns>Whether a prepared scalar is available.</returns>
        public ValueTask<bool> MoveNextAsync()
        {
            _index++;

            return _index == 0 && suspend ? FirstAsync() : ValueTask.FromResult(_index < values.Length);
        }

        /// <summary>Records reader completion without retaining any result or row history.</summary>
        /// <returns>The completed disposal.</returns>
        public ValueTask DisposeAsync()
        {
            // WHY: BenchmarkDotNet awaits invocations serially. Contributing the offered-row count once
            // avoids per-row shared-counter writes while still exposing incomplete or undisposed readers.
            owner.Disposals++;
            owner.RowsRead += Math.Min(_index + 1, values.Length);
            if (_index >= values.Length)
            {
                owner.EndOfStreams++;
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>Creates the comparison's deliberate one-off asynchronous boundary for each reader.</summary>
        private async ValueTask<bool> FirstAsync()
        {
            owner.Suspensions++;
            await Task.Yield();

            return values.Length != 0;
        }
    }
}
