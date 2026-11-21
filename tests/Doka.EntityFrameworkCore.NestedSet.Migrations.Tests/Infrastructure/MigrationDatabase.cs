using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Scaffolds, compiles and applies real migrations against one isolated database.</summary>
public sealed partial class MigrationDatabase : IAsyncDisposable
{
    private readonly Action<IServiceCollection, DbContext>? _configureDesignServices;
    private readonly Action<DbContextOptionsBuilder>? _configureOptions;
    private readonly string _connection;
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

    /// <summary>Applies the generated chain with the provider's ordinary IMigrator and migration history.</summary>
    /// <param name="chain">The compiled provider-generated migrations.</param>
    /// <param name="target">The target migration identifier; null applies the latest migration.</param>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>A task that completes after migration execution.</returns>
    public async Task MigrateAsync(
        MigrationChain chain,
        string? target,
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateContext(chain.Stage, chain.Assembly);
        await context
            .GetService<IMigrator>()
            .MigrateAsync(target, cancellationToken);
    }

    /// <summary>Reads every persisted hierarchy coordinate and payload in primary-key order.</summary>
    /// <param name="stage">The schema shape that currently exists in the database.</param>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>Immutable values suitable for exact before/after comparison.</returns>
    public async Task<MigrationNodeValue[]> ReadNodesAsync(
        MigrationStage stage,
        CancellationToken cancellationToken
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
                .ToArrayAsync(cancellationToken);
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
            .ToArrayAsync(cancellationToken);
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
        ?? Engine switch
        {
            "PostgreSql" => "public", "SqlServer" => "dbo", _ => command.Connection!.Database,
        };

    /// <summary>Normalizes provider catalog booleans, which may be represented as Boolean or integral values.</summary>
    private static bool ReadBoolean(
        DbDataReader reader,
        int ordinal
    ) => Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}
