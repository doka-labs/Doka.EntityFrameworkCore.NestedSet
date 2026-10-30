using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>Catalog validation retains compact scalar evidence instead of every materialized DTO.</summary>
    [Fact]
    public async Task CatalogReaderReleasesConsumedRowsBeforeEof()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request() with { Criteria = [] });
        var rows = new RetentionReader<SignalCatalogRow>(index => index % 2 == 0
            ? new SignalCatalogRow { Kind = 0, Id = snapshot.AnchorIds[0] }
            : new SignalCatalogRow { Kind = 1, Id = snapshot.ChannelIds[0], UnitCode = "count" });

        // Act
        await SignalReportValidation.RequireCatalogAsync(snapshot, rows, CancellationToken.None);

        // Assert
        Assert.InRange(rows.AliveAtEof, 0, 2);
        Assert.True(rows.Disposed);
    }

    /// <summary>Aggregate indexing keeps only tuple identities and scalar sums until the final grid exists.</summary>
    [Fact]
    public async Task GridReaderReleasesConsumedRowsBeforeEof()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var rows = new RetentionReader<SignalReportTotal>(index =>
            Total(SignalReportDataset.Id(index + 1), snapshot.ChannelIds[0], CapturePort.Pulse, index));

        // Act
        var cells = await SignalReportValidation.CreateGridAsync(snapshot, rows, CancellationToken.None);

        // Assert
        Assert.InRange(rows.AliveAtEof, 0, 2);
        Assert.True(rows.Disposed);
        Assert.Equal(new SignalReportCell(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, true, 0),
            Assert.Single(cells));
    }

    /// <summary>Generates fresh DTOs and observes retention only after clearing the reader's current row.</summary>
    private sealed class RetentionReader<T>(Func<int, T> create) : IAsyncEnumerable<T>, IAsyncEnumerator<T>
        where T : class
    {
        private readonly WeakReference<T>[] _rows = new WeakReference<T>[256];
        private int _index;

        /// <summary>Gets the current projection without a retained history of DTOs.</summary>
        public T Current { get; private set; } = null!;

        /// <summary>Gets the number of earlier DTOs still reachable at EOF after a full collection.</summary>
        internal int AliveAtEof { get; private set; }

        /// <summary>Gets whether the consumer disposed this one-use reader.</summary>
        internal bool Disposed { get; private set; }

        /// <summary>Creates one row at a time and checks collection before reporting successful EOF.</summary>
        public ValueTask<bool> MoveNextAsync()
        {
            Current = null!;
            if (_index == _rows.Length)
            {
                // WHY: This is a lifetime regression, not a GC throughput benchmark. A completed collection
                // distinguishes retained DTO histories from compact scalar indexes independently of GC timing.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                AliveAtEof = _rows.Count(reference => reference.TryGetTarget(out _));

                return ValueTask.FromResult(false);
            }

            Current = create(_index);
            _rows[_index++] = new WeakReference<T>(Current);

            return ValueTask.FromResult(true);
        }

        /// <summary>Provides the sole enumerator without adding a second retained reader state.</summary>
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return this;
        }

        /// <summary>Clears the last current value and records disposal.</summary>
        public ValueTask DisposeAsync()
        {
            Current = null!;
            Disposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
