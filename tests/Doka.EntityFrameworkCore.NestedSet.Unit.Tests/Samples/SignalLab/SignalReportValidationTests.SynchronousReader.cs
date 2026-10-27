using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using NonGenericEnumerable = System.Collections.IEnumerable;
using NonGenericEnumerator = System.Collections.IEnumerator;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>The synchronous collection adapter preserves ToDictionary's immediate duplicate rejection.</summary>
    [Fact]
    public void SynchronousGridRejectsDuplicateBeforeLaterEnumerationFailure()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var total = Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 7);
        var rows = new FailingTotals(total);

        // Act
        var error = Record.Exception(() => SignalReportValidation.CreateGrid(snapshot, rows));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.False(rows.LateFailureReached);
    }

    /// <summary>Distinguishes the first duplicate from a later non-database enumeration failure.</summary>
    private sealed class FailingTotals(SignalReportTotal total) : IReadOnlyCollection<SignalReportTotal>
    {
        /// <summary>Gets the advertised row count, including a row that fails during enumeration.</summary>
        public int Count => 3;

        /// <summary>Gets whether the reader advanced beyond the duplicate pair.</summary>
        internal bool LateFailureReached { get; private set; }

        /// <summary>Supplies the duplicate before a separate late enumeration failure.</summary>
        public IEnumerator<SignalReportTotal> GetEnumerator()
        {
            yield return total;
            yield return total;
            LateFailureReached = true;

            throw new InvalidOperationException("later synchronous enumeration failure");
        }

        /// <summary>Exposes the same sequence to non-generic collection consumers.</summary>
        NonGenericEnumerator NonGenericEnumerable.GetEnumerator() => GetEnumerator();
    }
}
