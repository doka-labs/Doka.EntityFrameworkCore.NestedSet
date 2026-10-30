using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes the original ranking command's session handler work before any EXPLAIN replay.</summary>
public abstract partial class GroupSupervisorTests
{
    private static readonly string[] s_rankingReadHandlers =
    [
        "Handler_read_first", "Handler_read_key", "Handler_read_last", "Handler_read_next",
        "Handler_read_prev", "Handler_read_rnd", "Handler_read_rnd_next",
    ];

    /// <summary>Reads common session-scoped engine counters from the caller's already pinned connection.</summary>
    /// <param name="context">The context with an explicitly opened connection for the measured operation.</param>
    /// <returns>Complete nonnegative read counters; malformed or missing observations fail closed.</returns>
    private static async Task<Dictionary<string, long>> ReadRankingHandlersAsync(App.UserGroupContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Rank handler observation requires the original open session.");
        }

        await using var command = connection.CreateCommand();
        // WHY: Only these read-request counters are session-scoped on both engines. MariaDB's GLOBAL-only
        // Handler_read_retry and its deleted-row counter cannot calibrate this cross-engine observation.
        command.CommandText = "SHOW SESSION STATUS WHERE Variable_name IN ("
            + "'Handler_read_first','Handler_read_key','Handler_read_last','Handler_read_next',"
            + "'Handler_read_prev','Handler_read_rnd','Handler_read_rnd_next')";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            var name = reader.GetString(0);
            if (!long.TryParse(reader.GetString(1), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                || !values.TryAdd(name, count))
            {
                throw new FormatException("The ranking session contains an invalid or duplicate read counter.");
            }
        }

        if (s_rankingReadHandlers.Any(name => !values.ContainsKey(name)))
        {
            throw new FormatException("The ranking session is missing a required read counter.");
        }

        return values;
    }

    /// <summary>Rejects resets and incomplete snapshots instead of reporting zero read requests.</summary>
    /// <param name="before">The complete counters immediately before the original consumer command.</param>
    /// <param name="after">The complete counters after that command and before EXPLAIN or other readers.</param>
    /// <returns>Per-counter nonnegative work deltas for this exact session window.</returns>
    private static Dictionary<string, long> RankingHandlerDelta(
        IReadOnlyDictionary<string, long> before,
        Dictionary<string, long> after
    )
    {
        var delta = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var counter in before)
        {
            if (!after.TryGetValue(counter.Key, out var value) || value < counter.Value)
            {
                throw new InvalidOperationException("The ranking session read counters changed identity or reset.");
            }

            delta.Add(counter.Key, value - counter.Value);
        }

        if (delta.Count != after.Count)
        {
            throw new InvalidOperationException("The ranking session read-counter set changed during execution.");
        }

        return delta;
    }

    /// <summary>Retains SHOW's own request overhead separately from a fully drained physical fixture lookup.</summary>
    /// <param name="context">The context whose connection remains open through the actual measured command.</param>
    /// <param name="source">The existing fixture source key used by the physical calibration read.</param>
    /// <returns>Raw status-only and lookup deltas, plus the number of completely drained lookup rows.</returns>
    private static async Task<(Dictionary<string, long> Empty, Dictionary<string, long> Lookup, int Rows)>
        CalibrateRankingHandlersAsync(App.UserGroupContext context, int source)
    {
        var before = await ReadRankingHandlersAsync(context);
        var empty = await ReadRankingHandlersAsync(context);
        var entity = context.Model.FindEntityType(typeof(App.User))
            ?? throw new InvalidOperationException("The rank calibration user mapping is missing.");

        var table = entity.GetTableName()
            ?? throw new InvalidOperationException("The rank calibration user table is missing.");

        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var property = entity.FindProperty(nameof(App.User.Id))
            ?? throw new InvalidOperationException("The rank calibration user key is missing.");

        var column = property.GetColumnName(store)
            ?? throw new InvalidOperationException("The rank calibration key column is missing.");

        var sql = context.GetService<ISqlGenerationHelper>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT {sql.DelimitIdentifier(column)} "
            + $"FROM {sql.DelimitIdentifier(table, entity.GetSchema())} "
            + $"WHERE {sql.DelimitIdentifier(column)} = @source";
        var key = command.CreateParameter();
        key.ParameterName = "source";
        key.DbType = DbType.Int32;
        key.Value = source;
        command.Parameters.Add(key);
        var rows = 0;
        await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
        {
            while (await reader.ReadAsync(CancellationToken.None))
            {
                rows++;
            }
        }

        var after = await ReadRankingHandlersAsync(context);

        return (RankingHandlerDelta(before, empty), RankingHandlerDelta(empty, after), rows);
    }

    /// <summary>Labels raw requests without equating them with rows, disk I/O, or equal engine totals.</summary>
    private static string FormatRankingHandlers(IReadOnlyDictionary<string, long>? values)
        => values is null ? "not applicable" : string.Join("; ", values
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .Select(row => FormattableString.Invariant($"{row.Key}={row.Value}")));
}
