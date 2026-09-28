namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingQueryShapeTests
{
    /// <summary>Records executed scalar ordinals without changing provider services or query expressions.</summary>
    private sealed class OrdinalProbe : DbCommandInterceptor
    {
        /// <summary>Gets reader observations in actual command execution order.</summary>
        internal List<OrdinalRead> Readers { get; } = [];

        /// <summary>Starts a fresh observation window without changing the underlying EF cache.</summary>
        internal void Reset() => Readers.Clear();

        /// <inheritdoc />
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (eventData.CommandSource != CommandSource.LinqQuery)
            {
                // WHY: Npgsql's native SaveChanges batch requires its concrete reader; only LINQ refresh is observed.
                return ValueTask.FromResult(result);
            }

            var observed = new OrdinalRead(command.CommandText);
            Readers.Add(observed);

            return ValueTask.FromResult<DbDataReader>(new OrdinalReader(result, observed));
        }
    }

    /// <summary>Retains executed SQL and actual returned ordinal values after the provider command is disposed.</summary>
    private sealed class OrdinalRead(string sql)
    {
        /// <summary>Gets the exact executed command text used to correlate this reader with the compilation probe.</summary>
        internal string Sql { get; } = sql;

        /// <summary>Gets observed numeric values from the projected first column, preserving duplicates.</summary>
        internal List<int> Ordinals { get; } = [];
    }

    /// <summary>Delegates provider reads and disposal while observing already-materialized scalar ordinals.</summary>
    private sealed class OrdinalReader(
        DbDataReader inner,
        OrdinalRead observed
    ) : DbDataReader
    {
        private bool _disposeStarted;

        /// <inheritdoc />
        public override object this[
            int ordinal
        ] =>
            inner[ordinal];

        /// <inheritdoc />
        public override object this[
            string name
        ] =>
            inner[name];

        /// <inheritdoc />
        public override int Depth => inner.Depth;

        /// <inheritdoc />
        public override int FieldCount => inner.FieldCount;

        /// <inheritdoc />
        public override int VisibleFieldCount => inner.VisibleFieldCount;

        /// <inheritdoc />
        public override bool HasRows => inner.HasRows;

        /// <inheritdoc />
        public override bool IsClosed => inner.IsClosed;

        /// <inheritdoc />
        public override int RecordsAffected => inner.RecordsAffected;

        /// <inheritdoc />
        public override async Task<bool> ReadAsync(
            CancellationToken cancellationToken
        )
        {
            var available = await inner
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            RecordOrdinal(available);

            return available;
        }

        /// <inheritdoc />
        public override bool Read()
        {
            // WHY: DbDataReader requires synchronous delegation too; the measured EF queries use ReadAsync.
            // ReSharper disable once MethodHasAsyncOverload
            var available = inner.Read();
            RecordOrdinal(available);

            return available;
        }

        /// <summary>Observes projected integer ordinals without coercing keys from preceding non-refresh queries.</summary>
        private void RecordOrdinal(
            bool available
        )
        {
            if (!available
                || inner.FieldCount == 0)
            {
                return;
            }

            // WHY: SQLite exposes integer expressions as Int64; both representations carry the same exact ordinal.
            switch (inner.GetValue(0))
            {
                case int value:
                    observed.Ordinals.Add(value);
                    break;
                case long value:
                    observed.Ordinals.Add(checked((int)value));
                    break;
            }
        }

        /// <inheritdoc />
        public override bool NextResult()
        {
            // WHY: The synchronous reader contract remains synchronous; measured operations use NextResultAsync.
            // ReSharper disable once MethodHasAsyncOverload
            return inner.NextResult();
        }

        /// <inheritdoc />
        public override Task<bool> NextResultAsync(
            CancellationToken cancellationToken
        ) => inner.NextResultAsync(cancellationToken);

        /// <inheritdoc />
        public override bool IsDBNull(
            int ordinal
        )
        {
            // WHY: EF scalar materialization requires the synchronous reader member; no database read is initiated here.
            // ReSharper disable once MethodHasAsyncOverload
            return inner.IsDBNull(ordinal);
        }

        /// <inheritdoc />
        public override Task<bool> IsDBNullAsync(
            int ordinal,
            CancellationToken cancellationToken
        ) => inner.IsDBNullAsync(ordinal, cancellationToken);

        /// <inheritdoc />
        public override object GetValue(
            int ordinal
        ) => inner.GetValue(ordinal);

        /// <inheritdoc />
        public override int GetValues(
            object[] values
        ) => inner.GetValues(values);

        /// <inheritdoc />
        public override T GetFieldValue<T>(
            int ordinal
        ) =>
            // WHY: EF scalar materialization requires the synchronous reader member; asynchronous delegation is separate.
            // ReSharper disable once MethodHasAsyncOverload
            inner.GetFieldValue<T>(ordinal);

        /// <inheritdoc />
        public override Task<T> GetFieldValueAsync<T>(
            int ordinal,
            CancellationToken cancellationToken
        ) => inner.GetFieldValueAsync<T>(ordinal, cancellationToken);

        /// <inheritdoc />
        public override string GetName(
            int ordinal
        ) => inner.GetName(ordinal);

        /// <inheritdoc />
        public override string GetDataTypeName(
            int ordinal
        ) => inner.GetDataTypeName(ordinal);

        /// <inheritdoc />
        [return:
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
                System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override Type GetFieldType(
            int ordinal
        ) => inner.GetFieldType(ordinal);

        /// <inheritdoc />
        public override int GetOrdinal(
            string name
        ) => inner.GetOrdinal(name);

        /// <inheritdoc />
        public override bool GetBoolean(
            int ordinal
        ) => inner.GetBoolean(ordinal);

        /// <inheritdoc />
        public override byte GetByte(
            int ordinal
        ) => inner.GetByte(ordinal);

        /// <inheritdoc />
        public override long GetBytes(
            int ordinal,
            long dataOffset,
            byte[]? buffer,
            int bufferOffset,
            int length
        ) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

        /// <inheritdoc />
        public override char GetChar(
            int ordinal
        ) => inner.GetChar(ordinal);

        /// <inheritdoc />
        public override long GetChars(
            int ordinal,
            long dataOffset,
            char[]? buffer,
            int bufferOffset,
            int length
        ) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

        /// <inheritdoc />
        public override Guid GetGuid(
            int ordinal
        ) => inner.GetGuid(ordinal);

        /// <inheritdoc />
        public override short GetInt16(
            int ordinal
        ) => inner.GetInt16(ordinal);

        /// <inheritdoc />
        public override int GetInt32(
            int ordinal
        ) => inner.GetInt32(ordinal);

        /// <inheritdoc />
        public override long GetInt64(
            int ordinal
        ) => inner.GetInt64(ordinal);

        /// <inheritdoc />
        public override DateTime GetDateTime(
            int ordinal
        ) => inner.GetDateTime(ordinal);

        /// <inheritdoc />
        public override decimal GetDecimal(
            int ordinal
        ) => inner.GetDecimal(ordinal);

        /// <inheritdoc />
        public override double GetDouble(
            int ordinal
        ) => inner.GetDouble(ordinal);

        /// <inheritdoc />
        public override float GetFloat(
            int ordinal
        ) => inner.GetFloat(ordinal);

        /// <inheritdoc />
        public override string GetString(
            int ordinal
        ) => inner.GetString(ordinal);

        /// <inheritdoc />
        public override Stream GetStream(
            int ordinal
        ) => inner.GetStream(ordinal);

        /// <inheritdoc />
        public override TextReader GetTextReader(
            int ordinal
        ) => inner.GetTextReader(ordinal);

        /// <inheritdoc />
        public override object GetProviderSpecificValue(
            int ordinal
        ) => inner.GetProviderSpecificValue(ordinal);

        /// <inheritdoc />
        public override int GetProviderSpecificValues(
            object[] values
        ) => inner.GetProviderSpecificValues(values);

        /// <inheritdoc />
        [return:
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
                System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override Type GetProviderSpecificFieldType(
            int ordinal
        ) => inner.GetProviderSpecificFieldType(ordinal);

        /// <inheritdoc />
        public override DataTable? GetSchemaTable() =>
            // WHY: The synchronous metadata contract must not initiate asynchronous work or block on a task.
            // ReSharper disable once MethodHasAsyncOverload
            inner.GetSchemaTable();

        /// <inheritdoc />
        public override Task<DataTable?> GetSchemaTableAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetSchemaTableAsync(cancellationToken);

        /// <inheritdoc />
        public override Task<System.Collections.ObjectModel.ReadOnlyCollection<DbColumn>> GetColumnSchemaAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetColumnSchemaAsync(cancellationToken);

        /// <inheritdoc />
        protected override DbDataReader GetDbDataReader(
            int ordinal
        ) => inner.GetData(ordinal);

        /// <inheritdoc />
        public override System.Collections.IEnumerator GetEnumerator() => inner.GetEnumerator();

        /// <inheritdoc />
        public override void Close()
        {
            if (_disposeStarted)
            {
                return;
            }

            // WHY: The synchronous reader contract remains synchronous; asynchronous callers use CloseAsync.
            // ReSharper disable once MethodHasAsyncOverload
            inner.Close();
        }

        /// <inheritdoc />
        public override Task CloseAsync() => _disposeStarted ? Task.CompletedTask : inner.CloseAsync();

        /// <inheritdoc />
        protected override void Dispose(
            bool disposing
        )
        {
            try
            {
                if (disposing && !_disposeStarted)
                {
                    _disposeStarted = true;
                    // WHY: Required synchronous disposal delegates once; asynchronous disposal has its own override.
                    // ReSharper disable once MethodHasAsyncOverload
                    inner.Dispose();
                }
            }
            finally
            {
                // WHY: Base disposal re-enters Close; the guard preserves its lifecycle without closing twice.
                base.Dispose(disposing);
            }
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (!_disposeStarted)
                {
                    _disposeStarted = true;
                    await inner
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                // WHY: The base async implementation re-enters Dispose; native cleanup must remain exactly once.
                await base
                    .DisposeAsync()
                    .ConfigureAwait(false);

                GC.SuppressFinalize(this);
            }
        }
    }
}
