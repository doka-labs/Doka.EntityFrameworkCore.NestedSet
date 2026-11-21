namespace Doka.EntityFrameworkCore.NestedSet.Diagnostics;

internal static partial class NestedSetTelemetry
{
    /// <summary>Retains low-cardinality measurements for exactly one observed public operation.</summary>
    private sealed class OperationState
    {
        private long _batchCount;
        private long _rebuildNodeCount = -1;
        private long _rowsAffected;

        /// <summary>Creates counters for one bounded provider family.</summary>
        internal OperationState(
            string provider
        )
        {
            Provider = provider;
        }

        /// <summary>Gets the bounded provider family.</summary>
        internal string Provider { get; }

        /// <summary>Gets whether at least one hierarchy write command reported a row count.</summary>
        internal bool HasRows { get; private set; }

        /// <summary>Gets the sum of hierarchy rows reported across write commands.</summary>
        internal long RowsAffected => Interlocked.Read(ref _rowsAffected);

        /// <summary>Gets the number of bounded write batches.</summary>
        internal long BatchCount => Interlocked.Read(ref _batchCount);

        /// <summary>Gets the rebuild node count when the operation is a rebuild.</summary>
        internal long? RebuildNodeCount
        {
            get
            {
                var value = Interlocked.Read(ref _rebuildNodeCount);

                return value < 0
                    ? null
                    : value;
            }
        }

        /// <summary>Adds a provider-reported hierarchy row count.</summary>
        internal void AddRows(
            long count
        )
        {
            HasRows = true;
            _ = Interlocked.Add(ref _rowsAffected, count);
        }

        /// <summary>Adds one bounded write batch.</summary>
        internal void AddBatch() => _ = Interlocked.Increment(ref _batchCount);

        /// <summary>Sets the complete inspected rebuild node count.</summary>
        internal void SetRebuildNodes(
            long count
        ) => Interlocked.Exchange(ref _rebuildNodeCount, count);
    }
}
