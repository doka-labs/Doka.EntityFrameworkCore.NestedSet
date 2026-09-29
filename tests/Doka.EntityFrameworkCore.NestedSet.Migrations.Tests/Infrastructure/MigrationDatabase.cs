using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Scaffolds, compiles and applies real migrations against one isolated database.</summary>
public sealed class MigrationDatabase : IAsyncDisposable
{
    private readonly string _connection;
    private readonly Action<DbContextOptionsBuilder>? _configureOptions;
    private readonly Action<IServiceCollection, DbContext>? _configureDesignServices;
    private readonly string? _file;

    internal MigrationDatabase(
        string engine,
        string connection,
        string? schema,
        Action<DbContextOptionsBuilder>? configureOptions,
        Action<IServiceCollection, DbContext>? configureDesignServices,
        string? file = null
    )
    {
        Engine = engine;
        Schema = schema;
        _connection = connection;
        _configureOptions = configureOptions;
        _configureDesignServices = configureDesignServices;
        _file = file;
    }

    /// <summary>Gets the database-engine discriminator.</summary>
    public string Engine { get; }

    /// <summary>Gets the explicit server schema, or null for SQLite.</summary>
    public string? Schema { get; }

    /// <summary>Creates an independent context using the real provider and optional integration.</summary>
    /// <param name="stage">The historical model version.</param>
    /// <param name="migrationsAssembly">The generated assembly, when migrations already exist.</param>
    /// <returns>A context owned by the caller.</returns>
    public MigrationContext CreateContext(
        MigrationStage stage = MigrationStage.Current,
        Assembly? migrationsAssembly = null
    )
    {
        var options = new DbContextOptionsBuilder<MigrationContext>().UseNestedSets();
        migrationsAssembly ??= typeof(MigrationContext).Assembly;

        if (Engine == "Sqlite")
        {
            options.UseSqlite(_connection, x => x.MigrationsAssembly(migrationsAssembly));
        }
        else if (Engine == "PostgreSql")
        {
            options.UseNpgsql(_connection, x => x.MigrationsAssembly(migrationsAssembly));
        }
        else if (Engine == "SqlServer")
        {
            options.UseSqlServer(_connection, x => x.MigrationsAssembly(migrationsAssembly));
        }
        else
        {
            var version = Engine == "MariaDb" ? DatabaseTestTargets.MariaDb : DatabaseTestTargets.MySql;

            options.UseMySql(_connection, version, x => x.MigrationsAssembly(migrationsAssembly));
        }

        // WHY: Every compiled test assembly and schema is unique; retaining their service providers globally
        // would accumulate test-only models across otherwise independent cases.
        options.EnableServiceProviderCaching(false);
        options.ReplaceService<IModelCacheKeyFactory, MigrationModelCacheKeyFactory>();
        options.UseNestedSets();
        _configureOptions?.Invoke(options);

        return new MigrationContext(options.Options, stage, Schema);
    }

    /// <summary>Runs EF's scaffolder and compiles the complete generated migration, metadata and snapshot.</summary>
    /// <param name="stage">The model to compare against the preceding snapshot.</param>
    /// <param name="name">The new migration name.</param>
    /// <param name="priorChain">The compiled preceding migrations and snapshot, if any.</param>
    /// <returns>A complete compiled migration chain.</returns>
    public MigrationChain Scaffold(
        MigrationStage stage,
        string name,
        MigrationChain? priorChain = null
    )
    {
        using var context = CreateContext(stage, priorChain?.Assembly);
        var services = new ServiceCollection();
        services.AddDbContextDesignTimeServices(context);

        var provider = Assembly.Load(context.Database.ProviderName!);
        var attribute = provider.GetCustomAttribute<DesignTimeProviderServicesAttribute>()
            ?? throw new InvalidOperationException("The provider does not declare its design-time services.");

        var serviceType = provider.GetType(attribute.TypeName, throwOnError: true)!;
        var providerServices = (IDesignTimeServices)Activator.CreateInstance(serviceType)!;
        providerServices.ConfigureDesignTimeServices(services);
        services.AddEntityFrameworkDesignTimeServices();
        _configureDesignServices?.Invoke(services, context);

        using var serviceProvider = services.BuildServiceProvider();
        var scaffolder = serviceProvider.GetRequiredService<IMigrationsScaffolder>();
        var migration = scaffolder.ScaffoldMigration(name, "Doka.Generated", "Migrations", "C#", false);
        var sources = priorChain?.Sources.ToList() ?? [];
        sources.Add(migration.MigrationCode);
        sources.Add(migration.MetadataCode);

        var migrationIds = priorChain?.MigrationIds.ToList() ?? [];
        migrationIds.Add(migration.MigrationId);

        return new MigrationChain(sources, migration.SnapshotCode, migrationIds, migration.MigrationCode, stage);
    }

    /// <summary>Applies the generated chain with the provider's ordinary IMigrator and migration history.</summary>
    /// <param name="chain">The compiled provider-generated migrations.</param>
    /// <param name="target">The target migration identifier; null applies the latest migration.</param>
    /// <returns>A task that completes after migration execution.</returns>
    public async Task MigrateAsync(
        MigrationChain chain,
        string? target = null
    )
    {
        await using var context = CreateContext(chain.Stage, chain.Assembly);
        await context
            .GetService<IMigrator>()
            .MigrateAsync(target, CancellationToken.None);
    }

    /// <summary>Stores two hierarchy rows for the requested historical model.</summary>
    /// <param name="stage">The schema shape that currently exists in the database.</param>
    /// <returns>A task that completes after the seed rows are saved.</returns>
    public async Task SeedAsync(
        MigrationStage stage = MigrationStage.Current
    )
    {
        if (stage != MigrationStage.Baseline)
        {
            await InsertNodeDirectlyAsync(
                new MigrationNode
                {
                    NodeId = 41,
                    Scope = 7,
                    TreeId = 11,
                    Left = 1,
                    Right = 4,
                    Payload = "parent payload",
                    Category = 2,
                });
            await InsertNodeDirectlyAsync(
                new MigrationNode
                {
                    NodeId = 42,
                    Scope = 7,
                    TreeId = 11,
                    ParentId = 41,
                    Left = 2,
                    Right = 3,
                    Depth = 1,
                    Payload = "child payload",
                    Category = 1,
                });

            return;
        }

        await using var context = CreateContext(stage);
        await context
            .Set<BaselineMigrationNode>(MigrationContext.EntityTypeName)
            .AddRangeAsync(
                [
                    new BaselineMigrationNode
                    {
                        NodeId = 41,
                        Scope = 7,
                        TreeId = 11,
                        Left = 1,
                        Right = 4,
                        Payload = "parent payload",
                        Category = 2,
                    },
                    new BaselineMigrationNode
                    {
                        NodeId = 42,
                        Scope = 7,
                        TreeId = 11,
                        ParentId = 41,
                        Left = 2,
                        Right = 3,
                        Depth = 1,
                        Payload = "child payload",
                        Category = 1,
                    },
                ],
                CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Inserts one current row directly so database constraints can be qualified independently of save logic.
    /// </summary>
    /// <param name="node">The complete physical row to insert.</param>
    /// <returns>A task that completes with the affected-row count.</returns>
    public async Task<int> InsertNodeDirectlyAsync(
        MigrationNode node
    )
    {
        await using var context = CreateContext();
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(MigrationContext.TableName, Schema);
        var columns = new[]
        {
            "entry_id",
            "tree_scope",
            "tree_id",
            "parent_entry",
            "left_bound",
            "right_bound",
            "entry_depth",
            "sibling_position",
            "entry_payload",
            "entry_category",
        };

        var command = "INSERT INTO "
            + table
            + " ("
            + string.Join(", ", columns.Select(sql.DelimitIdentifier))
            + ") VALUES ("
            + string.Join(
                ", ",
                Enumerable
                    .Range(0, columns.Length)
                    .Select(index => "{" + index + "}"))
            + ")";

        var parent = context
            .Database
            .GetDbConnection()
            .CreateCommand()
            .CreateParameter();
        parent.ParameterName = "@parent";
        parent.DbType = DbType.Int32;
        parent.Value = node.ParentId is null ? DBNull.Value : node.ParentId.Value;

        return await context.Database.ExecuteSqlRawAsync(
            command,
            [
                node.NodeId,
                node.Scope,
                node.TreeId,
                parent,
                node.Left,
                node.Right,
                node.Depth,
                node.Position,
                node.Payload,
                node.Category,
            ],
            CancellationToken.None);
    }

    /// <summary>Reads every persisted hierarchy coordinate and payload in primary-key order.</summary>
    /// <param name="stage">The schema shape that currently exists in the database.</param>
    /// <returns>Immutable values suitable for exact before/after comparison.</returns>
    public async Task<MigrationNodeValue[]> ReadNodesAsync(
        MigrationStage stage = MigrationStage.Current
    )
    {
        await using var context = CreateContext(stage);

        if (stage == MigrationStage.Baseline)
        {
            return await context
                .Set<BaselineMigrationNode>(MigrationContext.EntityTypeName)
                .OrderBy(x => x.NodeId)
                .Select(x => new MigrationNodeValue(
                    x.NodeId,
                    x.Scope,
                    x.TreeId,
                    x.ParentId,
                    x.Left,
                    x.Right,
                    x.Depth,
                    x.Position,
                    x.Payload,
                    x.Category))
                .ToArrayAsync(CancellationToken.None);
        }

        return await context
            .Set<MigrationNode>()
            .OrderBy(x => x.NodeId)
            .Select(x => new MigrationNodeValue(
                x.NodeId,
                x.Scope,
                x.TreeId,
                x.ParentId,
                x.Left,
                x.Right,
                x.Depth,
                x.Position,
                x.Payload,
                x.Category))
            .ToArrayAsync(CancellationToken.None);
    }

    /// <summary>Reads physical nonprimary indexes and their ordered columns from the engine's catalog.</summary>
    /// <returns>Indexes in database-name order.</returns>
    public async Task<MigrationIndex[]> ReadIndexesAsync()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
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
                ?? (Engine switch
                {
                    "PostgreSql" => "public", "SqlServer" => "dbo", _ => command.Connection!.Database,
                });

            AddParameter(command, "@schema", catalogSchema);
        }

        var columns =
            new Dictionary<string, (List<string> Columns, List<bool> Descending, bool Unique, bool Ordinary)>(
                StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
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
    /// <returns>Checks in database-name order.</returns>
    public async Task<MigrationCheck[]> ReadChecksAsync()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        if (Engine == "Sqlite")
        {
            command.CommandText = "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = @table";
            AddParameter(command, "@table", MigrationContext.TableName);
            var definition = (string?)await command.ExecuteScalarAsync(CancellationToken.None) ?? "";

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
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            checks.Add(new MigrationCheck(reader.GetString(0), reader.GetString(1)));
        }

        return checks.ToArray();
    }

    /// <summary>Reads physical foreign keys with dependent and principal columns in ordinal order.</summary>
    /// <returns>Foreign keys in database-name order.</returns>
    public async Task<MigrationForeignKey[]> ReadForeignKeysAsync()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
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

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
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
    /// <returns>A case-insensitive map from physical column name to provider type name.</returns>
    public async Task<IReadOnlyDictionary<string, string>> ReadColumnTypesAsync()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
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
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            types.Add(reader.GetString(0), reader.GetString(1));
        }

        return types;
    }

    /// <summary>Releases temporary SQLite files; the fixture owns and removes server databases.</summary>
    /// <returns>An already completed value task.</returns>
    public ValueTask DisposeAsync()
    {
        if (_file is not null)
        {
            File.Delete(_file);
            File.Delete(_file + "-wal");
            File.Delete(_file + "-shm");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Binds catalog names as values instead of interpolating application metadata into SQL.</summary>
    private static void AddParameter(
        DbCommand command,
        string name,
        string value
    )
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Returns the catalog schema or database containing the mapped hierarchy table.</summary>
    private string CatalogSchema(
        DbCommand command
    ) => Schema
        ?? (Engine switch
        {
            "PostgreSql" => "public", "SqlServer" => "dbo", _ => command.Connection!.Database,
        });

    /// <summary>Normalizes provider catalog booleans, which may be represented as Boolean or integral values.</summary>
    private static bool ReadBoolean(
        DbDataReader reader,
        int ordinal
    ) => Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}

/// <summary>The physical, ordered definition of a nonprimary index.</summary>
/// <param name="Name">The physical database index name.</param>
/// <param name="Columns">The indexed column names in key order.</param>
/// <param name="Descending">Whether each corresponding key column is descending.</param>
/// <param name="IsUnique">Whether the index enforces uniqueness.</param>
/// <param name="IsOrdinary">Whether every key is full length, ascending, valid and unfiltered on a B-tree.</param>
public sealed record MigrationIndex(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<bool> Descending,
    bool IsUnique,
    bool IsOrdinary
);

/// <summary>A physical check constraint read from a provider catalog.</summary>
/// <param name="Name">The physical constraint name.</param>
/// <param name="Expression">The provider-rendered check expression.</param>
public sealed record MigrationCheck(
    string Name,
    string Expression
);

/// <summary>A physical foreign key read from a provider catalog.</summary>
/// <param name="Name">The physical constraint name or SQLite identifier.</param>
/// <param name="DependentColumns">The dependent columns in key order.</param>
/// <param name="PrincipalColumns">The principal columns in key order.</param>
/// <param name="DeleteAction">The provider-reported delete action.</param>
public sealed record MigrationForeignKey(
    string Name,
    IReadOnlyList<string> DependentColumns,
    IReadOnlyList<string> PrincipalColumns,
    string DeleteAction
);

/// <summary>An immutable view of all persisted values in one hierarchy row.</summary>
/// <param name="NodeId">The primary key.</param>
/// <param name="Scope">The application scope.</param>
/// <param name="TreeId">The stable tree identity, or zero before the historical upgrade.</param>
/// <param name="ParentId">The parent identifier.</param>
/// <param name="Left">The left boundary.</param>
/// <param name="Right">The right boundary.</param>
/// <param name="Depth">The depth below the root.</param>
/// <param name="Position">The sibling position.</param>
/// <param name="Payload">The application payload.</param>
/// <param name="Category">The application-owned secondary sibling-order value.</param>
public sealed record MigrationNodeValue(
    int NodeId,
    int Scope,
    int TreeId,
    int? ParentId,
    long Left,
    long Right,
    int Depth,
    long Position,
    string Payload,
    int Category
);
