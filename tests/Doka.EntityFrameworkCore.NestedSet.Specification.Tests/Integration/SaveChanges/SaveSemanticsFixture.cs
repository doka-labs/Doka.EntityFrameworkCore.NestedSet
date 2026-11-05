namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Reuses real providers while each tracker test family owns and resets its independent tables.</summary>
public sealed class SaveSemanticsFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>
    ///     Creates tables on first use and then resets the caller's scenario before each serialized test.
    /// </summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="engine">The real database engine used by the case.</param>
    /// <param name="prepare">The family's table creation, clearing and seed callback with the caller's token.</param>
    /// <returns>The fixture-owned database, which individual tests must not dispose.</returns>
    public async Task<TestDatabase> PrepareAsync(
        string engine,
        Func<TestDatabase, bool, CancellationToken, Task> prepare,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_databases.TryGetValue(engine, out var database))
        {
            await prepare(database, false, cancellationToken);

            return database;
        }

        database = await TestDatabase.CreateAsync(engine, cancellationToken: cancellationToken);

        try
        {
            await prepare(database, true, cancellationToken);
            _databases.Add(engine, database);

            return database;
        }
        catch (Exception preparationFailure)
        {
            // WHY: A partially created schema must not become the fixture's reusable baseline for later cases.
            try
            {
                await database.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Save fixture preparation and cleanup both failed.",
                    preparationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    /// <summary>
    ///     Clones provider options for another context type with independently observable statement batches.
    /// </summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="database">The fixture database whose established provider setup is retained.</param>
    /// <param name="interceptors">Observers registered only on the new context.</param>
    /// <returns>Context-neutral options using one save command per batch.</returns>
    public static async Task<DbContextOptions> OptionsAsync(
        TestDatabase database,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var source = database.CreateContext();
        var extensions = source.GetService<IDbContextOptions>().Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
            .ConfigureTestWarnings();

        var connection = source.Database.GetConnectionString()!;

        // WHY: One command per batch lets late-failure tests prove that both identity INSERT and payload UPDATE
        // completed before the structural failure, independent of each provider's ordinary batching defaults.
        switch (source.Database.ProviderName)
        {
            case "Microsoft.EntityFrameworkCore.Sqlite":
                options.UseSqlite(connection, provider => provider.MaxBatchSize(1));
                break;
            case "Npgsql.EntityFrameworkCore.PostgreSQL":
                options.UseNpgsql(connection, provider => provider.MaxBatchSize(1));
                break;
            case "Doka.EntityFrameworkCore.MySql":
                new MySqlDbContextOptionsBuilder(options).MaxBatchSize(1);
                break;
            case "Microsoft.EntityFrameworkCore.SqlServer":
                options.UseSqlServer(connection, provider => provider.MaxBatchSize(1));
                break;
            default:
                throw new InvalidOperationException("The save semantics fixture requires a supported provider.");
        }

        return options.AddInterceptors(interceptors).Options;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.DisposeAsync();
        }
    }
}
