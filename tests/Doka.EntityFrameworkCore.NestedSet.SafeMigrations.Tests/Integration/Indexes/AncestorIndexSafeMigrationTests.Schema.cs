using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class AncestorIndexSafeMigrationTests
{
    /// <summary>Reads generated values without relying on tracked entity state.</summary>
    private static Task<bool[]> ReadFlagsAsync(
        AncestorIndexContext context
    ) => context
        .Set<MigrationNode>()
        .OrderBy(node => node.NodeId)
        .Select(node => EF.Property<bool>(node, AncestorIndexContext.FlagProperty))
        .ToArrayAsync(CancellationToken.None);

    /// <summary>Checks every replayed operation rather than only the additive index.</summary>
    private static void AssertMatchingReplay(
        SafeMigrationRunReport report,
        int expectedOperations
    )
    {
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        Assert.Equal(expectedOperations, report.Assessments.Count);
        Assert.All(
            report.Assessments,
            assessment =>
            {
                Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
                Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
                Assert.True(assessment.PostconditionSatisfied);
            });
    }

    /// <summary>Checks the complete additive key and its provider-specific partial-index contract.</summary>
    private static void AssertAncestorIndex(
        MigrationIndex[] indexes,
        string engine
    )
    {
        var index = Assert.Single(
            indexes,
            candidate => candidate.Columns.SequenceEqual(
            [
                "tree_scope",
                AncestorIndexContext.FlagColumn,
                "tree_id",
                "right_bound",
                "left_bound",
                "entry_id",
            ]));

        Assert.False(index.IsUnique);
        Assert.All(index.Descending, Assert.False);
        Assert.Equal(engine != "PostgreSql", index.IsOrdinary);
    }

    /// <summary>Checks actual generated storage and catalog facets independently of EF's model claims.</summary>
    private static void AssertGeneratedColumn(
        GeneratedColumnSchema schema,
        string engine
    )
    {
        var storeType = engine switch
        {
            "Sqlite" => "INTEGER",
            "MySql" or "MariaDb" => "tinyint",
            "PostgreSql" => "boolean",
            "SqlServer" => "bit",
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        Assert.Equal(storeType, schema.StoreType, ignoreCase: true);
        // WHY: MariaDB cannot declare generated NOT NULL, and SQL Server reports inferred computed
        // nullability. Their adapters prove the required result from the exact expression and operands.
        Assert.Equal(engine is "MariaDb" or "SqlServer", schema.IsNullable);
        Assert.Equal(engine == "PostgreSql", schema.IsStored);
        Assert.Contains("left_bound", schema.Expression, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("right_bound", schema.Expression, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASE", schema.Expression, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            long.MaxValue.ToString(CultureInfo.InvariantCulture),
            schema.Expression,
            StringComparison.Ordinal);
    }

    /// <summary>Reads the actual engine definition, including columns SQLite omits from table_info.</summary>
    private async Task<GeneratedColumnSchema> ReadGeneratedColumnAsync(
        AncestorIndexContext context,
        string engine
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = engine switch
        {
            "Sqlite" =>
                "SELECT c.type, 1 - c.\"notnull\", c.hidden = 3, t.sql "
                + "FROM pragma_table_xinfo(@table) c JOIN sqlite_schema t ON t.name = @table "
                + "AND t.type = 'table' WHERE c.name = @column AND c.hidden IN (2, 3)",
            "PostgreSql" =>
                "SELECT c.data_type, CASE WHEN c.is_nullable = 'YES' THEN 1 ELSE 0 END, "
                + "CASE WHEN c.is_generated = 'ALWAYS' THEN 1 ELSE 0 END, c.generation_expression "
                + "FROM information_schema.columns c WHERE c.table_name = @table "
                + "AND c.table_schema = @schema AND c.column_name = @column AND c.is_generated = 'ALWAYS'",
            "SqlServer" => "SELECT ty.name, c.is_nullable, c.is_persisted, c.definition "
                + "FROM sys.computed_columns c JOIN sys.tables t ON t.object_id = c.object_id "
                + "JOIN sys.schemas s ON s.schema_id = t.schema_id "
                + "JOIN sys.types ty ON ty.user_type_id = c.user_type_id "
                + "WHERE t.name = @table AND s.name = @schema AND c.name = @column",
            _ => "SELECT DATA_TYPE, CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END, "
                + "CASE WHEN EXTRA LIKE '%STORED GENERATED%' OR EXTRA LIKE '%PERSISTENT%' THEN 1 ELSE 0 END, "
                + "GENERATION_EXPRESSION FROM information_schema.COLUMNS "
                + "WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @schema AND COLUMN_NAME = @column "
                + "AND GENERATION_EXPRESSION <> ''",
        };

        AddCatalogParameters(command, context, engine);
        AddParameter(command, "@column", AncestorIndexContext.FlagColumn);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        if (!await reader.ReadAsync(CancellationToken.None))
        {
            throw new InvalidOperationException("The expected physical generated column is missing.");
        }

        var schema = new GeneratedColumnSchema(
            reader.GetString(0),
            Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture),
            Convert.ToBoolean(reader.GetValue(2), CultureInfo.InvariantCulture),
            reader.GetString(3));

        if (await reader.ReadAsync(CancellationToken.None))
        {
            throw new InvalidOperationException("The generated-column catalog lookup was not unique.");
        }

        _output.WriteLine($"{engine} generated column: {schema}");

        return schema;
    }

    /// <summary>Checks that preflight did not create the pending application table.</summary>
    private static async Task<int> ReadHierarchyTableCountAsync(
        AncestorIndexContext context,
        string engine
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = engine == "Sqlite"
            ? "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = @table"
            : "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @schema";

        AddCatalogParameters(command, context, engine);
        var count = await command.ExecuteScalarAsync(CancellationToken.None);

        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    /// <summary>Detects any physical flag created by a supposedly read-only populated preflight.</summary>
    private static async Task<int> ReadGeneratedColumnCountAsync(
        AncestorIndexContext context,
        string engine
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = engine == "Sqlite"
            ? "SELECT COUNT(*) FROM pragma_table_xinfo(@table) WHERE name = @column"
            : "SELECT COUNT(*) FROM information_schema.COLUMNS "
            + "WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @schema AND COLUMN_NAME = @column";

        AddCatalogParameters(command, context, engine);
        AddParameter(command, "@column", AncestorIndexContext.FlagColumn);
        var count = await command.ExecuteScalarAsync(CancellationToken.None);

        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    /// <summary>Binds table ownership as values so catalog queries cannot cross schemas.</summary>
    private static void AddCatalogParameters(
        DbCommand command,
        AncestorIndexContext context,
        string engine
    )
    {
        AddParameter(command, "@table", MigrationContext.TableName);
        if (engine != "Sqlite")
        {
            var schema = context.Schema
                ?? (engine switch
                {
                    "PostgreSql" => "public", "SqlServer" => "dbo", _ => command.Connection!.Database,
                });

            AddParameter(command, "@schema", schema);
        }
    }

    /// <summary>Prevents metadata values from being interpolated into catalog SQL.</summary>
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

    /// <summary>Retains independently observed physical facets for exact replay equality.</summary>
    private sealed record GeneratedColumnSchema(
        string StoreType,
        bool IsNullable,
        bool IsStored,
        string Expression
    );
}
