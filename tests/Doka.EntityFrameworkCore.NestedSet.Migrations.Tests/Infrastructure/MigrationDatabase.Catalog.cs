namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class MigrationDatabase
{
    /// <summary>Reads physical nonprimary indexes and their ordered columns from the engine's catalog.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>Indexes in database-name order.</returns>
    public async Task<MigrationIndex[]> ReadIndexesAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = Engine switch
        {
            "Sqlite" =>
                "SELECT il.name, ii.name, il.[unique], il.partial = 0, ii.desc "
                + "FROM pragma_index_list(@table) il JOIN pragma_index_xinfo(il.name) ii "
                + "WHERE il.origin <> 'pk' AND ii.[key] = 1 ORDER BY il.name, ii.seqno",
            "PostgreSql" =>
                "SELECT i.relname, a.attname, ix.indisunique, "
                + "am.amname = 'btree' AND ix.indpred IS NULL AND ix.indisvalid "
                + ", (ix.indoption[(k.position - 1)::int] & 1) = 1 "
                + "FROM pg_index ix JOIN pg_class t ON t.oid = ix.indrelid "
                + "JOIN pg_namespace n ON n.oid = t.relnamespace JOIN pg_class i ON i.oid = ix.indexrelid "
                + "JOIN pg_am am ON am.oid = i.relam "
                + "CROSS JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY k(attnum, position) "
                + "JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum "
                + "WHERE t.relname = @table AND n.nspname = @schema AND NOT ix.indisprimary "
                + "ORDER BY i.relname, k.position",
            "SqlServer" => "SELECT i.name, c.name, i.is_unique, "
                + "CASE WHEN i.type = 2 AND i.has_filter = 0 AND i.is_disabled = 0 "
                + "THEN 1 ELSE 0 END, ic.is_descending_key "
                + "FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id "
                + "JOIN sys.schemas s ON s.schema_id = t.schema_id "
                + "JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id "
                + "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id "
                + "WHERE t.name = @table AND s.name = @schema AND i.is_primary_key = 0 "
                + "AND ic.key_ordinal > 0 ORDER BY i.name, ic.key_ordinal",
            _ => "SELECT INDEX_NAME, COLUMN_NAME, NON_UNIQUE = 0, "
                + "SUB_PART IS NULL AND INDEX_TYPE = 'BTREE', COLLATION = 'D' "
                + "FROM information_schema.STATISTICS "
                + "WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @schema AND INDEX_NAME <> 'PRIMARY' "
                + "ORDER BY INDEX_NAME, SEQ_IN_INDEX",
        };

        AddParameter(command, "@table", MigrationContext.TableName);

        if (Engine != "Sqlite")
        {
            // WHY: Catalog lookup needs the connection database for unqualified MySQL mappings,
            // while unqualified PostgreSQL and SQL Server mappings use their default schemas.
            var catalogSchema = Schema
                ?? Engine switch
                {
                    "PostgreSql" => "public", "SqlServer" => "dbo", _ => command.Connection!.Database,
                };

            AddParameter(command, "@schema", catalogSchema);
        }

        var columns =
            new Dictionary<string, (List<string> Columns, List<bool> Descending, bool Unique, bool Ordinary)>(
                StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);

            if (!columns.TryGetValue(name, out var entry))
            {
                entry = ([], [], ReadBoolean(reader, 2), true);
                columns.Add(name, entry);
            }

            entry.Columns.Add(reader.GetString(1));
            entry.Descending.Add(ReadBoolean(reader, 4));
            columns[name] = (entry.Columns, entry.Descending, entry.Unique, entry.Ordinary && ReadBoolean(reader, 3));
        }

        return columns
            .Select(x => new MigrationIndex(
                x.Key,
                x.Value.Columns.ToArray(),
                x.Value.Descending.ToArray(),
                x.Value.Unique,
                x.Value.Ordinary))
            .ToArray();
    }

    /// <summary>Reads every physical check constraint from the provider catalog.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>Checks in database-name order.</returns>
    public async Task<MigrationCheck[]> ReadChecksAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        if (Engine == "Sqlite")
        {
            command.CommandText = "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = @table";
            AddParameter(command, "@table", MigrationContext.TableName);
            var definition = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "";

            return Regex
                .Matches(
                    definition,
                    "CONSTRAINT\\s+[\\\"`\\[]?(?<name>[^\\\"`\\]\\s]+)[\\\"`\\]]?\\s+CHECK\\s*\\((?<sql>[^)]*)\\)",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
                .Select(match => new MigrationCheck(match.Groups["name"].Value, match.Groups["sql"].Value))
                .OrderBy(check => check.Name, StringComparer.Ordinal)
                .ToArray();
        }

        command.CommandText = Engine switch
        {
            "PostgreSql" =>
                "SELECT c.conname, pg_get_constraintdef(c.oid) "
                + "FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid "
                + "JOIN pg_namespace n ON n.oid = t.relnamespace "
                + "WHERE c.contype = 'c' AND t.relname = @table AND n.nspname = @schema ORDER BY c.conname",
            "SqlServer" => "SELECT c.name, c.definition FROM sys.check_constraints c "
                + "JOIN sys.tables t ON t.object_id = c.parent_object_id "
                + "JOIN sys.schemas s ON s.schema_id = t.schema_id "
                + "WHERE t.name = @table AND s.name = @schema ORDER BY c.name",
            _ => "SELECT c.CONSTRAINT_NAME, c.CHECK_CLAUSE FROM information_schema.CHECK_CONSTRAINTS c "
                + "JOIN information_schema.TABLE_CONSTRAINTS t "
                + "ON t.CONSTRAINT_SCHEMA = c.CONSTRAINT_SCHEMA AND t.CONSTRAINT_NAME = c.CONSTRAINT_NAME "
                + "WHERE t.TABLE_NAME = @table AND t.TABLE_SCHEMA = @schema "
                + "AND t.CONSTRAINT_TYPE = 'CHECK' ORDER BY c.CONSTRAINT_NAME",
        };

        AddParameter(command, "@table", MigrationContext.TableName);
        AddParameter(command, "@schema", CatalogSchema(command));
        var checks = new List<MigrationCheck>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            checks.Add(new MigrationCheck(reader.GetString(0), reader.GetString(1)));
        }

        return checks.ToArray();
    }

    /// <summary>Reads physical foreign keys with dependent and principal columns in ordinal order.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>Foreign keys in database-name order.</returns>
    public async Task<MigrationForeignKey[]> ReadForeignKeysAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = Engine switch
        {
            "Sqlite" =>
                "SELECT CAST(id AS TEXT), seq, [from], [to], on_delete "
                + "FROM pragma_foreign_key_list(@table) ORDER BY id, seq",
            "PostgreSql" =>
                "SELECT c.conname, k.position, dependent.attname, principal.attname, "
                + "CASE c.confdeltype WHEN 'r' THEN 'RESTRICT' WHEN 'c' THEN 'CASCADE' "
                + "WHEN 'n' THEN 'SET NULL' WHEN 'd' THEN 'SET DEFAULT' ELSE 'NO ACTION' END "
                + "FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid "
                + "JOIN pg_namespace n ON n.oid = t.relnamespace "
                + "CROSS JOIN LATERAL unnest(c.conkey, c.confkey) WITH ORDINALITY "
                + "AS k(dependent_number, principal_number, position) "
                + "JOIN pg_attribute dependent ON dependent.attrelid = c.conrelid "
                + "AND dependent.attnum = k.dependent_number "
                + "JOIN pg_attribute principal ON principal.attrelid = c.confrelid "
                + "AND principal.attnum = k.principal_number "
                + "WHERE c.contype = 'f' AND t.relname = @table AND n.nspname = @schema "
                + "ORDER BY c.conname, k.position",
            "SqlServer" => "SELECT fk.name, fkc.constraint_column_id, dependent.name, principal.name, "
                + "fk.delete_referential_action_desc FROM sys.foreign_keys fk "
                + "JOIN sys.tables t ON t.object_id = fk.parent_object_id "
                + "JOIN sys.schemas s ON s.schema_id = t.schema_id "
                + "JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id "
                + "JOIN sys.columns dependent ON dependent.object_id = t.object_id "
                + "AND dependent.column_id = fkc.parent_column_id "
                + "JOIN sys.columns principal ON principal.object_id = fk.referenced_object_id "
                + "AND principal.column_id = fkc.referenced_column_id "
                + "WHERE t.name = @table AND s.name = @schema "
                + "ORDER BY fk.name, fkc.constraint_column_id",
            _ => "SELECT k.CONSTRAINT_NAME, k.ORDINAL_POSITION, k.COLUMN_NAME, k.REFERENCED_COLUMN_NAME, "
                + "r.DELETE_RULE FROM information_schema.KEY_COLUMN_USAGE k "
                + "JOIN information_schema.REFERENTIAL_CONSTRAINTS r "
                + "ON r.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA AND r.CONSTRAINT_NAME = k.CONSTRAINT_NAME "
                + "WHERE k.TABLE_NAME = @table AND k.TABLE_SCHEMA = @schema "
                + "AND k.REFERENCED_TABLE_NAME = @table ORDER BY k.CONSTRAINT_NAME, k.ORDINAL_POSITION",
        };

        AddParameter(command, "@table", MigrationContext.TableName);

        if (Engine != "Sqlite")
        {
            AddParameter(command, "@schema", CatalogSchema(command));
        }

        var foreignKeys =
            new Dictionary<string, (List<string> Dependent, List<string> Principal, string DeleteAction)>(
                StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);

            if (!foreignKeys.TryGetValue(name, out var entry))
            {
                entry = ([], [], reader.GetString(4));
                foreignKeys.Add(name, entry);
            }

            entry.Dependent.Add(reader.GetString(2));
            entry.Principal.Add(reader.GetString(3));
        }

        return foreignKeys
            .Select(entry => new MigrationForeignKey(
                entry.Key,
                entry.Value.Dependent.ToArray(),
                entry.Value.Principal.ToArray(),
                entry.Value.DeleteAction))
            .ToArray();
    }

    /// <summary>Reads physical column type names for the current hierarchy table.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>A case-insensitive map from physical column name to provider type name.</returns>
    public async Task<IReadOnlyDictionary<string, string>> ReadColumnTypesAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = Engine switch
        {
            "Sqlite" => "SELECT name, type FROM pragma_table_info(@table) ORDER BY cid",
            "PostgreSql" => "SELECT column_name, data_type FROM information_schema.columns "
                + "WHERE table_name = @table AND table_schema = @schema ORDER BY ordinal_position",
            _ => "SELECT COLUMN_NAME, DATA_TYPE FROM information_schema.COLUMNS "
                + "WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @schema ORDER BY ORDINAL_POSITION",
        };

        AddParameter(command, "@table", MigrationContext.TableName);

        if (Engine != "Sqlite")
        {
            AddParameter(command, "@schema", CatalogSchema(command));
        }

        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            types.Add(reader.GetString(0), reader.GetString(1));
        }

        return types;
    }
}
