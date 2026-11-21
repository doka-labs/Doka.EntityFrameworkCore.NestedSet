using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>
    /// Counts supported native handler-row traversals, including repeated reads of derived and physical rows.
    /// </summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static async Task<long> NativeTraversalCounterAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SHOW SESSION STATUS WHERE Variable_name IN "
        + "('Handler_read_first','Handler_read_key','Handler_read_last','Handler_read_next',"
        + "'Handler_read_prev','Handler_read_rnd','Handler_read_rnd_next')";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long total = 0;
        var counters = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            total = checked(total + Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture));
            counters++;
        }

        return counters != 7
            ? throw new InvalidOperationException(
                "The native engine did not expose every required handler-row traversal counter.")
            : total;
    }

    /// <summary>Retains a planning-only diagnostic without repeating a timed-out native statement.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="capture">The command and materialization capture.</param>
    private static async Task<string> FailurePlanAsync(
        DbContext context,
        EnterpriseProbe capture,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var command = context
                .Database
                .GetDbConnection()
                .CreateCommand();

            command.CommandText = "EXPLAIN FORMAT=JSON " + capture.Commands.Single();

            foreach (var parameter in capture.ParameterBindings.Single())
            {
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);

            return reader.GetString(0);
        }
        catch (Exception exception)
        {
            // WHY: A secondary diagnostic failure must preserve the original native failure reported by the test.
            return "Static native plan unavailable: "
                + exception.GetType()
                    .FullName;
        }
    }

    /// <summary>Reads a native session-work counter on the same open measured connection.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="name">The synthetic object or scenario name.</param>
    private async Task<long> MariaSessionCounterAsync(
        DbContext context,
        string name,
        CancellationToken cancellationToken
    )
    {
        if (Engine != "MariaDb")
        {
            return 0;
        }

        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SHOW SESSION STATUS LIKE @name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = name;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("MariaDB did not expose the requested native work counter.");
        }

        return Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
    }
}
