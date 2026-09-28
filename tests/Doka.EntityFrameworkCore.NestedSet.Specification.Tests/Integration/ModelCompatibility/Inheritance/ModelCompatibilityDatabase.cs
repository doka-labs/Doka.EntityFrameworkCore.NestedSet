namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares one real database per provider across distinct model-compatibility tables.</summary>
public sealed class ModelCompatibilityDatabase : IAsyncDisposable
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);
    private readonly HashSet<(string Engine, Type Model)> _createdTables = [];

    /// <summary>Builds provider options for mapping checks that do not connect to a database.</summary>
    internal static DbContextOptions<TContext> Options<TContext>(
        string engine
    )
        where TContext : DbContext => Options<TContext>(
        engine,
        "Server=localhost;Database=model_compatibility;User ID=unused;Password=unused");

    /// <summary>Creates a mapped context and its tables inside an independently owned test database.</summary>
    /// <param name="engine">The relational provider to use.</param>
    /// <param name="create">Constructs the mapped context from configured provider options.</param>
    /// <param name="interceptors">Optional observers for this context only.</param>
    /// <returns>The context and its ready schema.</returns>
    internal async Task<TContext> CreateContextAsync<TContext>(
        string engine,
        Func<DbContextOptions<TContext>, TContext> create,
        params IInterceptor[] interceptors
    )
        where TContext : DbContext
    {
        if (!_databases.TryGetValue(engine, out var database))
        {
            database = await TestDatabase.CreateAsync(engine);
            _databases.Add(engine, database);
        }

        await using var source = database.CreateContext();
        var connection = source.Database.GetConnectionString()!;
        var options = Options<TContext>(engine, connection);
        var context = create(
            interceptors.Length == 0
                ? options
                : new DbContextOptionsBuilder<TContext>(options).AddInterceptors(interceptors).Options);

        try
        {
            if (_createdTables.Add((engine, typeof(TContext))))
            {
                // WHY: Each model has distinct tables, so a provider container can be reused without sharing rows.
                await context
                    .GetService<IRelationalDatabaseCreator>()
                    .CreateTablesAsync(CancellationToken.None);
            }

            return context;
        }
        catch
        {
            _createdTables.Remove((engine, typeof(TContext)));
            await context.DisposeAsync();

            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>
    /// Builds provider options while preserving defaults unless a collection mode is explicitly requested.
    /// </summary>
    /// <typeparam name="TContext">The fixture's actual mapped context.</typeparam>
    /// <param name="engine">The established test provider.</param>
    /// <param name="connection">The fixture-owned connection string.</param>
    /// <param name="collectionMode">An optional application collection-translation override.</param>
    /// <returns>Provider options with the ordinary nested-set registration.</returns>
    internal static DbContextOptions<TContext> Options<TContext>(
        string engine,
        string connection,
        ParameterTranslationMode? collectionMode = null
    )
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().ConfigureTestWarnings();
        var mode = collectionMode.GetValueOrDefault();

        switch (engine)
        {
            case "Sqlite":
                options.UseSqlite(
                    connection,
                    collectionMode.HasValue ? provider => provider.UseParameterizedCollectionMode(mode) : null);
                break;
            case "MySql":
                options.UseMySql(
                    connection,
                    DatabaseTestTargets.MySql,
                    collectionMode.HasValue ? provider => provider.UseParameterizedCollectionMode(mode) : null);
                break;
            case "MariaDb":
                options.UseMySql(
                    connection,
                    DatabaseTestTargets.MariaDb,
                    collectionMode.HasValue ? provider => provider.UseParameterizedCollectionMode(mode) : null);
                break;
            case "PostgreSql":
                options.UseNpgsql(
                    connection,
                    collectionMode.HasValue ? provider => provider.UseParameterizedCollectionMode(mode) : null);
                break;
            case "SqlServer":
                options.UseSqlServer(
                    connection,
                    collectionMode.HasValue ? provider => provider.UseParameterizedCollectionMode(mode) : null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        options.UseNestedSets();

        return options.Options;
    }
}
