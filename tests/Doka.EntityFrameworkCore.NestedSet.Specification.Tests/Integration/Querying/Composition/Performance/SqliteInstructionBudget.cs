using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Interrupts a test-owned SQLite read after a fixed number of approximate instruction quanta.</summary>
/// <remarks>
/// The caller owns a fresh, open, nonpooled connection without another progress handler. Observations include
/// SQLite preparation callbacks and approximate VM work; they do not measure rows, elapsed time, or physical I/O.
/// </remarks>
internal sealed class SqliteInstructionBudget : IDisposable
{
    /// <summary>The approximate instruction interval between native progress callbacks.</summary>
    internal const int Quantum = 1_000;

    private readonly sqlite3 _handle;
    private readonly long _maximumInstructions;
    private bool _disposed;

    /// <summary>Installs a bounded callback on the caller's otherwise unobserved native connection.</summary>
    /// <param name="connection">The fresh, explicitly opened, nonpooled test connection.</param>
    /// <param name="maximumInstructions">The fixed allowance chosen before measuring the query.</param>
    internal SqliteInstructionBudget(
        SqliteConnection connection,
        long maximumInstructions
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumInstructions, Quantum);
        _handle = connection.Handle ?? throw new InvalidOperationException("The SQLite connection must be open.");
        _maximumInstructions = maximumInstructions;

        // WHY: Returning a native interrupt code bounds CPU work even though SQLite executes async reads
        // synchronously. Throwing through a native callback would bypass the provider's normal error boundary.
        raw.sqlite3_progress_handler(_handle, Quantum,
            static state => ((SqliteInstructionBudget)state).Observe(), this);
    }

    /// <summary>The callback count multiplied by its approximate instruction interval.</summary>
    internal long ObservedInstructions { get; private set; }

    /// <summary>Whether the read crossed its predeclared allowance and requested a native interrupt.</summary>
    internal bool Exceeded => ObservedInstructions > _maximumInstructions;

    /// <summary>Removes this test-owned callback before diagnostics or connection disposal.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        raw.sqlite3_progress_handler(_handle, 0, null, null);
        _disposed = true;
    }

    /// <summary>Counts fixed quanta without allocating, issuing SQL, or throwing across the native boundary.</summary>
    private int Observe()
    {
        ObservedInstructions += Quantum;

        return Exceeded ? 1 : 0;
    }
}
