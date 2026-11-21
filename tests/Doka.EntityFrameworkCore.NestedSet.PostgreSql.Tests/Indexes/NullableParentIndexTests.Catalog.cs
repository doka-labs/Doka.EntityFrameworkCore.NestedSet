using System.Collections.Generic;
using System.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

public sealed partial class NullableParentIndexTests
{
    /// <summary>Reads ordered native key columns and validity without trusting EF's design metadata alone.</summary>
    private static async Task<NativePartialIndex?> ReadNativeIndexAsync(
        DbContext context,
        IEntityType entity,
        string indexName,
        CancellationToken cancellationToken
    )
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 0;
        command.CommandText = """
                              SELECT attribute.attname, pg_get_expr(definition.indpred, definition.indrelid, true),
                                  definition.indisvalid, definition.indisready, definition.indisunique
                              FROM pg_catalog.pg_index AS definition
                              JOIN pg_catalog.pg_class AS table_class ON table_class.oid = definition.indrelid
                              JOIN pg_catalog.pg_namespace AS table_schema
                                  ON table_schema.oid = table_class.relnamespace
                              JOIN pg_catalog.pg_class AS index_class ON index_class.oid = definition.indexrelid
                              CROSS JOIN LATERAL unnest(definition.indkey) WITH ORDINALITY AS index_key(attnum, ordinal)
                              JOIN pg_catalog.pg_attribute AS attribute
                                  ON attribute.attrelid = table_class.oid AND attribute.attnum = index_key.attnum
                              WHERE table_class.relname = @table
                                  AND table_schema.nspname = COALESCE(@schema, current_schema())
                                  AND index_class.relname = @index AND index_key.ordinal <= definition.indnkeyatts
                              ORDER BY index_key.ordinal
                              """;

        foreach (var (name, value) in new[]
                 {
                     ("table", entity.GetTableName()),
                     ("schema", entity.GetSchema()),
                     ("index", indexName),
                 })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = DbType.String;
            parameter.Value = value is null ? DBNull.Value : value;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new List<string>();
        string? predicate = null;
        var valid = false;
        var ready = false;
        var unique = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
            predicate = reader.IsDBNull(1) ? null : reader.GetString(1);
            valid = reader.GetBoolean(2);
            ready = reader.GetBoolean(3);
            unique = reader.GetBoolean(4);
        }

        return names.Count == 0 ? null : new NativePartialIndex(names.ToArray(), predicate, valid, ready, unique);
    }

    /// <summary>Retains the catalog facts needed to distinguish an available partial index from its name.</summary>
    private sealed record NativePartialIndex(
        string[] Columns,
        string? Predicate,
        bool Valid,
        bool Ready,
        bool Unique
    );
}
