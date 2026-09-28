namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns reusable engines with distinct tables for configured-access and generated-key contracts.</summary>
public sealed class ContractFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Returns an initialized database with all contract rows removed.</summary>
    /// <param name="engine">The selected relational engine.</param>
    /// <returns>The fixture-owned engine, which callers must not dispose.</returns>
    public async Task<TestDatabase> ResetAsync(
        string engine
    )
    {
        if (!_databases.TryGetValue(engine, out var database))
        {
            database = await TestDatabase.CreateAsync(engine);
            _databases.Add(engine, database);
            await using var fields = CreateFieldContext(engine, database);
            await fields
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);

            await using var generated = CreateGeneratedContext(engine, database);
            await generated
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);
        }

        await database.ResetAsync();
        await using var fieldReset = CreateFieldContext(engine, database);

        // WHY: The convention-created restrictive self-FK must be unlinked before fixture cleanup on providers
        // that validate referential integrity immediately.
        await fieldReset
            .Set<FieldNode>()
            .Where(node => node.ParentId != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (int?)null),
                CancellationToken.None);

        await fieldReset
            .Set<FieldNode>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await fieldReset.ClearNestedSetTreeRegistriesAsync(CancellationToken.None);
        await using var generatedReset = CreateGeneratedContext(engine, database);
        await generatedReset
            .Set<TreeNode>()
            .Where(node => node.Parent != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.Parent, (int?)null),
                CancellationToken.None);

        await generatedReset
            .Set<TreeNode>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await generatedReset.ClearNestedSetTreeRegistriesAsync(CancellationToken.None);

        return database;
    }

    /// <summary>Creates an isolated configured-access context after resetting its fixture.</summary>
    /// <param name="engine">The selected relational engine.</param>
    /// <returns>A context owned by the calling test.</returns>
    public async Task<FieldContext> CreateFieldContextAsync(
        string engine
    )
    {
        var database = await ResetAsync(engine);

        return CreateFieldContext(engine, database);
    }

    /// <summary>Creates an isolated generated-identity context after resetting its fixture.</summary>
    /// <param name="engine">The selected relational engine.</param>
    /// <returns>A context owned by the calling test.</returns>
    public async Task<InvalidContext> CreateGeneratedContextAsync(
        string engine
    )
    {
        var database = await ResetAsync(engine);

        return CreateGeneratedContext(engine, database);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>Creates options against an existing fixture without owning its connection.</summary>
    private static DbContextOptionsBuilder<TContext> Options<TContext>(
        string engine,
        TestDatabase database
    )
        where TContext : DbContext
    {
        // WHY: Copy the connection string, not the open connection; each test context owns its transaction lifetime.
        using var source = database.CreateContext();
        var connection = source.Database.GetConnectionString()!;
        var builder = new DbContextOptionsBuilder<TContext>()
            .ConfigureTestWarnings()
            .UseNestedSets();

        switch (engine)
        {
            case "Sqlite":
                builder.UseSqlite(connection);
                break;
            case "PostgreSql":
                builder.UseNpgsql(connection);
                break;
            case "SqlServer":
                builder.UseSqlServer(connection);
                break;
            case "MySql":
                builder.UseMySql(connection, DatabaseTestTargets.MySql);
                break;
            case "MariaDb":
                builder.UseMySql(connection, DatabaseTestTargets.MariaDb);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        return builder;
    }

    /// <summary>Creates field-access options without changing the fixture's schema.</summary>
    private static FieldContext CreateFieldContext(
        string engine,
        TestDatabase database
    ) => new(
        Options<FieldContext>(engine, database)
            .Options);

    /// <summary>Creates the generated-identity variant with the existing shared model-cache identity.</summary>
    private static InvalidContext CreateGeneratedContext(
        string engine,
        TestDatabase database
    ) => new(
        Options<InvalidContext>(engine, database)
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .Options,
        "generated-key");
}
