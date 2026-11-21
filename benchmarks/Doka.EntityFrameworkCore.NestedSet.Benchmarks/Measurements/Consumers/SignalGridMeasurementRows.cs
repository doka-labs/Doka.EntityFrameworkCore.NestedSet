using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Supplies one prepared completed-read source per restored benchmark iteration.</summary>
internal sealed class SignalGridMeasurementRows
    : IAsyncEnumerable<SignalReportTotal>, IAsyncEnumerator<SignalReportTotal>
{
    private readonly SignalReportTotal[] _rows;
    private readonly Task<bool>? _failure;
    private int _index = -1;

    /// <summary>Retains scalar inputs and an optional already-created late failure.</summary>
    internal SignalGridMeasurementRows(
        SignalReportTotal[] rows,
        Exception? failure = null
    )
    {
        _rows = rows;
        _failure = failure is null ? null : Task.FromException<bool>(failure);
    }

    /// <summary>Gets whether the actual consumer disposed the current reader.</summary>
    internal bool Disposed { get; private set; }

    /// <summary>Gets the number of actual scalar rows consumed in this iteration.</summary>
    internal int RowsRead { get; private set; }

    /// <summary>Restores one iteration's position without changing inputs or allocating another row array.</summary>
    internal void Reset()
    {
        _index = -1;
        Disposed = false;
        RowsRead = 0;
    }

    /// <inheritdoc />
    public SignalReportTotal Current => _rows[_index];

    /// <inheritdoc />
    public IAsyncEnumerator<SignalReportTotal> GetAsyncEnumerator(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return this;
    }

    /// <inheritdoc />
    public ValueTask<bool> MoveNextAsync()
    {
        _index++;
        if (_index < _rows.Length)
        {
            RowsRead++;

            return ValueTask.FromResult(true);
        }

        return _failure is null ? ValueTask.FromResult(false) : new ValueTask<bool>(_failure);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Disposed = true;

        return ValueTask.CompletedTask;
    }
}
