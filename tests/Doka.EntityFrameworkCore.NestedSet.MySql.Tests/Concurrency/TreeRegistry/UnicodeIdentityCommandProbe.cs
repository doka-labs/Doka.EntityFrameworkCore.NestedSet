using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Counts operation commands and verifies the actual scalar or JSON identity transport.</summary>
internal sealed class UnicodeIdentityCommandProbe : DbCommandInterceptor
{
    /// <summary>Gets the attempted commands after the test's arrangement completed.</summary>
    internal int CommandCount { get; private set; }

    /// <summary>Gets the native ordering commands using the wide JSON_TABLE transport.</summary>
    internal int JsonRowsetCommands { get; private set; }

    /// <summary>Excludes schema creation and arrangement queries from the measured operation boundary.</summary>
    internal void Reset()
    {
        CommandCount = 0;
        JsonRowsetCommands = 0;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Observe(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Observe(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        Observe(command);

        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Classifies the actual SQL command without retaining values or depending on incidental numbering.
    /// </summary>
    private void Observe(
        DbCommand command
    )
    {
        CommandCount++;

        if (command.CommandText.Contains("JSON_TABLE", StringComparison.OrdinalIgnoreCase))
        {
            JsonRowsetCommands++;
        }
    }
}
