namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns isolated ordering tables while reusing the established real database provisioning.</summary>
public sealed class OrderingKeyFixture : IAsyncLifetime
{
    /// <summary>Retains one disposable database for each engine used by this test class.</summary>
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>Clears the fixture's key-specific tables and returns a context owned by the caller.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="engine">The requested relational engine.</param>
    /// <returns>A context connected to the empty key-ordering tables.</returns>
    public async Task<OrderingKeyContext> ResetAsync(
        string engine,
        CancellationToken cancellationToken
    )
    {
        if (!_databases.TryGetValue(engine, out var database))
        {
            database = await TestDatabase.CreateAsync(engine, cancellationToken: cancellationToken);
            _databases.Add(engine, database);

            try
            {
                await using var setup = await CreateContextAsync(engine, cancellationToken: cancellationToken);
                await setup
                    .GetService<IRelationalDatabaseCreator>()
                    .CreateTablesAsync(cancellationToken);
            }
            catch (Exception initializationFailure)
            {
                // WHY: Canceled table creation must not leave a partially initialized reusable database.
                _databases.Remove(engine);

                try
                {
                    await database.DisposeAsync();
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "Ordering fixture initialization and cleanup both failed.",
                        initializationFailure,
                        cleanupFailure);
                }

                throw;
            }
        }

        var context = await CreateContextAsync(engine, cancellationToken: cancellationToken);

        try
        {
            await context
                .Set<OrderingKeyNode<string, string?>>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (string?)null),
                    cancellationToken);

            await context
                .Set<OrderingKeyNode<string, string?>>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingKeyNode<Guid, Guid?>>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (Guid?)null),
                    cancellationToken);

            await context
                .Set<OrderingKeyNode<Guid, Guid?>>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingKeyNode<byte[], byte[]?>>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (byte[]?)null),
                    cancellationToken);

            await context
                .Set<OrderingKeyNode<byte[], byte[]?>>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingKeyNode<int, int?>>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    cancellationToken);

            await context
                .Set<OrderingKeyNode<int, int?>>()
                .ExecuteDeleteAsync(cancellationToken);

            await NestedSetTestInfrastructure.ClearRegistriesAsync(context, cancellationToken);

            return context;
        }
        catch (Exception resetFailure)
        {
            // WHY: Ownership transfers only after a complete reset; cleanup cannot use the canceled token.
            try
            {
                await context.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Ordering fixture reset and context cleanup both failed.",
                    resetFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    /// <summary>Uses the provisioned connection with this fixture's independent ordered EF model.</summary>
    private async Task<OrderingKeyContext> CreateContextAsync(
        string engine,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var source = _databases[engine]
            .CreateContext();

        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<OrderingKeyContext>().ConfigureTestWarnings();

        switch (engine)
        {
            case "Sqlite":
                options.UseSqlite(connection, provider => provider
                    .CommandTimeout(0)
                    .UseNestedSets());
                break;
            case "PostgreSql":
                options.UseNpgsql(connection, provider => provider
                    .CommandTimeout(0)
                    .UseNestedSets());
                break;
            case "SqlServer":
                options.UseSqlServer(connection, provider => provider
                    .CommandTimeout(0)
                    .UseNestedSets());
                break;
            case "MySql":
                options.UseMySql(connection, DatabaseTestTargets.MySql, provider => provider
                    .UseNestedSets());
                break;
            case "MariaDb":
                options.UseMySql(connection, DatabaseTestTargets.MariaDb, provider => provider
                    .UseNestedSets());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        FunctionalTestContextPolicy.ConfigureOptions(options);

        return new OrderingKeyContext(options.Options);
    }
}
