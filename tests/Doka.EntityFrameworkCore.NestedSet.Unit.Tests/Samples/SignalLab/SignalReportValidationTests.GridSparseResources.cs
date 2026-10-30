using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>A failed sparse reader does not reserve its 786432-cell grain before successful completion.</summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public async Task SparseReaderFailureDoesNotReserveTheCartesianGrid()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = Enumerable.Range(1, 512).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(101, 512).Select(SignalReportDataset.Id).ToArray(),
            Sources = [CapturePort.Pulse, CapturePort.Sweep, CapturePort.Burst],
        };

        SignalReportTotal[] totals =
        [
            Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 0),
            Total(snapshot.AnchorIds[^1], snapshot.ChannelIds[^1], CapturePort.Burst, 7),
        ];

        var failure = new InvalidOperationException("late sparse reader failure");
        var rows = new SparseFailureReader(totals, failure);
        var thread = Environment.CurrentManagedThreadId;

        // Act
        // WHY: This source offers only completed ValueTasks. Current-thread bytes isolate this operation
        // from parallel tests without blocking async execution; the thread assertion guards that premise.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = await Record.ExceptionAsync(() => SignalReportValidation.CreateGridAsync(snapshot,
            rows, CancellationToken.None));

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        output.WriteLine($"Sparse late failure: cells={512 * 512 * 3}; rows=2; allocated={allocated} B.");
        Assert.Same(failure, error);
        Assert.True(rows.Disposed);
        Assert.Equal(thread, Environment.CurrentManagedThreadId);
        Assert.InRange(allocated, 0, 256 * 1024);
    }

    /// <summary>
    /// Supplies two completed reads and a completed fault, without introducing scheduler allocations.
    /// </summary>
    private sealed class SparseFailureReader(
        SignalReportTotal[] rows,
        Exception failure
    ) : IAsyncEnumerable<SignalReportTotal>, IAsyncEnumerator<SignalReportTotal>
    {
        private readonly Task<bool> _failure = Task.FromException<bool>(failure);
        private int _index = -1;

        /// <summary>Gets the latest sparse scalar projection.</summary>
        public SignalReportTotal Current => rows[_index];

        /// <summary>Gets whether the failed reader was asynchronously disposed.</summary>
        internal bool Disposed { get; private set; }

        /// <summary>Returns completed values or the prepared late failure.</summary>
        public ValueTask<bool> MoveNextAsync()
        {
            _index++;

            return _index < rows.Length ? ValueTask.FromResult(true) : new ValueTask<bool>(_failure);
        }

        /// <summary>Exposes the sole prepared enumerator after checking the supplied cancellation token.</summary>
        public IAsyncEnumerator<SignalReportTotal> GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return this;
        }

        /// <summary>Records disposal without scheduling additional work.</summary>
        public ValueTask DisposeAsync()
        {
            Disposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
