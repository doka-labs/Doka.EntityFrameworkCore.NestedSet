using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes the first successfully read locked row while preserving provider reads and disposal.</summary>
internal sealed class ConcurrentCapacityLockReader : DbDataReader
{
    private readonly DbDataReader _inner;
    private readonly Action _acquired;
    private readonly Func<CancellationToken, Task>? _afterReadAsync;
    private bool _observed;
    private bool _disposeStarted;

    /// <summary>Wraps one registry reader with lock observation and an optional asynchronous writer barrier.</summary>
    internal ConcurrentCapacityLockReader(
        DbDataReader inner,
        Action acquired,
        Func<CancellationToken, Task>? afterReadAsync = null
    )
    {
        _inner = inner;
        _acquired = acquired;
        _afterReadAsync = afterReadAsync;
    }

    /// <inheritdoc />
    public override object this[
        int ordinal
    ] =>
        _inner[ordinal];

    /// <inheritdoc />
    public override object this[
        string name
    ] =>
        _inner[name];

    /// <inheritdoc />
    public override int Depth => _inner.Depth;

    /// <inheritdoc />
    public override int FieldCount => _inner.FieldCount;

    /// <inheritdoc />
    public override int VisibleFieldCount => _inner.VisibleFieldCount;

    /// <inheritdoc />
    public override bool HasRows => _inner.HasRows;

    /// <inheritdoc />
    public override bool IsClosed => _inner.IsClosed;

    /// <inheritdoc />
    public override int RecordsAffected => _inner.RecordsAffected;

    /// <inheritdoc />
    public override async Task<bool> ReadAsync(
        CancellationToken cancellationToken
    )
    {
        var available = await _inner
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ObserveFirstRow(available)
            && _afterReadAsync is not null)
        {
            // WHY: A successful row read proves lock acquisition; keeping that transaction here makes the
            // capacity barrier observe simultaneously held locks rather than merely dispatched queries.
            await _afterReadAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return available;
    }

    /// <inheritdoc />
    public override bool Read()
    {
        var available = _inner.Read();
        ObserveFirstRow(available);

        // WHY: The synchronous reader contract stays synchronous. The positive capacity probe uses ReadAsync,
        // and its final arrival-count assertion rejects a path that bypasses the asynchronous barrier.
        return available;
    }

    /// <summary>Counts the first returned row once without consuming or replacing any provider value.</summary>
    private bool ObserveFirstRow(
        bool available
    )
    {
        if (!available || _observed)
        {
            return false;
        }

        _observed = true;
        _acquired();

        return true;
    }

    /// <inheritdoc />
    public override bool NextResult() => _inner.NextResult();

    /// <inheritdoc />
    public override Task<bool> NextResultAsync(
        CancellationToken cancellationToken
    ) => _inner.NextResultAsync(cancellationToken);

    /// <inheritdoc />
    public override bool IsDBNull(
        int ordinal
    ) => _inner.IsDBNull(ordinal);

    /// <inheritdoc />
    public override Task<bool> IsDBNullAsync(
        int ordinal,
        CancellationToken cancellationToken
    ) => _inner.IsDBNullAsync(ordinal, cancellationToken);

    /// <inheritdoc />
    public override object GetValue(
        int ordinal
    ) => _inner.GetValue(ordinal);

    /// <inheritdoc />
    public override int GetValues(
        object[] values
    ) => _inner.GetValues(values);

    /// <inheritdoc />
    public override T GetFieldValue<T>(
        int ordinal
    ) => _inner.GetFieldValue<T>(ordinal);

    /// <inheritdoc />
    public override Task<T> GetFieldValueAsync<T>(
        int ordinal,
        CancellationToken cancellationToken
    ) => _inner.GetFieldValueAsync<T>(ordinal, cancellationToken);

    /// <inheritdoc />
    public override string GetName(
        int ordinal
    ) => _inner.GetName(ordinal);

    /// <inheritdoc />
    public override string GetDataTypeName(
        int ordinal
    ) => _inner.GetDataTypeName(ordinal);

    /// <inheritdoc />
    [return:
        DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    public override Type GetFieldType(
        int ordinal
    ) => _inner.GetFieldType(ordinal);

    /// <inheritdoc />
    public override int GetOrdinal(
        string name
    ) => _inner.GetOrdinal(name);

    /// <inheritdoc />
    public override bool GetBoolean(
        int ordinal
    ) => _inner.GetBoolean(ordinal);

    /// <inheritdoc />
    public override byte GetByte(
        int ordinal
    ) => _inner.GetByte(ordinal);

    /// <inheritdoc />
    public override long GetBytes(
        int ordinal,
        long dataOffset,
        byte[]? buffer,
        int bufferOffset,
        int length
    ) => _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc />
    public override char GetChar(
        int ordinal
    ) => _inner.GetChar(ordinal);

    /// <inheritdoc />
    public override long GetChars(
        int ordinal,
        long dataOffset,
        char[]? buffer,
        int bufferOffset,
        int length
    ) => _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc />
    public override Guid GetGuid(
        int ordinal
    ) => _inner.GetGuid(ordinal);

    /// <inheritdoc />
    public override short GetInt16(
        int ordinal
    ) => _inner.GetInt16(ordinal);

    /// <inheritdoc />
    public override int GetInt32(
        int ordinal
    ) => _inner.GetInt32(ordinal);

    /// <inheritdoc />
    public override long GetInt64(
        int ordinal
    ) => _inner.GetInt64(ordinal);

    /// <inheritdoc />
    public override DateTime GetDateTime(
        int ordinal
    ) => _inner.GetDateTime(ordinal);

    /// <inheritdoc />
    public override decimal GetDecimal(
        int ordinal
    ) => _inner.GetDecimal(ordinal);

    /// <inheritdoc />
    public override double GetDouble(
        int ordinal
    ) => _inner.GetDouble(ordinal);

    /// <inheritdoc />
    public override float GetFloat(
        int ordinal
    ) => _inner.GetFloat(ordinal);

    /// <inheritdoc />
    public override string GetString(
        int ordinal
    ) => _inner.GetString(ordinal);

    /// <inheritdoc />
    public override Stream GetStream(
        int ordinal
    ) => _inner.GetStream(ordinal);

    /// <inheritdoc />
    public override TextReader GetTextReader(
        int ordinal
    ) => _inner.GetTextReader(ordinal);

    /// <inheritdoc />
    public override object GetProviderSpecificValue(
        int ordinal
    ) => _inner.GetProviderSpecificValue(ordinal);

    /// <inheritdoc />
    public override int GetProviderSpecificValues(
        object[] values
    ) => _inner.GetProviderSpecificValues(values);

    /// <inheritdoc />
    [return:
        DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    public override Type GetProviderSpecificFieldType(
        int ordinal
    ) => _inner.GetProviderSpecificFieldType(ordinal);

    /// <inheritdoc />
    public override DataTable? GetSchemaTable() => _inner.GetSchemaTable();

    /// <inheritdoc />
    public override Task<DataTable?> GetSchemaTableAsync(
        CancellationToken cancellationToken = default
    ) => _inner.GetSchemaTableAsync(cancellationToken);

    /// <inheritdoc />
    public override Task<ReadOnlyCollection<DbColumn>> GetColumnSchemaAsync(
        CancellationToken cancellationToken = default
    ) => _inner.GetColumnSchemaAsync(cancellationToken);

    /// <inheritdoc />
    protected override DbDataReader GetDbDataReader(
        int ordinal
    ) => _inner.GetData(ordinal);

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => _inner.GetEnumerator();

    /// <inheritdoc />
    public override void Close()
    {
        if (!_disposeStarted)
        {
            _inner.Close();
        }
    }

    /// <inheritdoc />
    public override Task CloseAsync() => _disposeStarted ? Task.CompletedTask : _inner.CloseAsync();

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
                _inner.Dispose();
            }
        }
        finally
        {
            // WHY: Base disposal re-enters Close; the guard preserves native disposal exactly once.
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
                await _inner
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            // WHY: The base implementation re-enters Dispose; the inner provider reader is already released.
            await base
                .DisposeAsync()
                .ConfigureAwait(false);

            GC.SuppressFinalize(this);
        }
    }
}
