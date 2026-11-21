namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns isolated scope-anchor test tables on the existing real-engine infrastructure.</summary>
public sealed class OrderingLockFixture : IAsyncLifetime
{
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

    /// <summary>Returns an empty anchored-ordering context without disturbing another fixture's tables.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="engine">The real relational engine.</param>
    /// <returns>A context owned by the test.</returns>
    public async Task<OrderingLockContext> ResetAsync(
        string engine,
        CancellationToken cancellationToken
    )
    {
        if (!_databases.ContainsKey(engine))
        {
            var database = await TestDatabase.CreateAsync(engine, cancellationToken: cancellationToken);
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
            await UnlinkAsync<int, FirstLockHierarchy>(context, cancellationToken: cancellationToken);
            await UnlinkAsync<int, SecondLockHierarchy>(context, cancellationToken: cancellationToken);
            await UnlinkAsync<string, FirstLockHierarchy>(context, cancellationToken: cancellationToken);
            await UnlinkAsync<byte[], FirstLockHierarchy>(context, cancellationToken: cancellationToken);
            await context
                .Set<OrderingLockNode<int, FirstLockHierarchy>>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingLockNode<int, SecondLockHierarchy>>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingLockNode<string, FirstLockHierarchy>>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingLockNode<byte[], FirstLockHierarchy>>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingLockAnchor<int>>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingLockAnchor<string>>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingLockAnchor<byte[]>>()
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

    /// <summary>Removes restrictive self references before provider-portable fixture cleanup.</summary>
    private static Task<int> UnlinkAsync<TScope, THierarchy>(
        OrderingLockContext context,
        CancellationToken cancellationToken
    )
        where TScope : notnull => context
        .Set<OrderingLockNode<TScope, THierarchy>>()
        .Where(node => node.ParentId != null)
        .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.ParentId, (int?)null), cancellationToken);

    /// <summary>Creates another independently tracked context with a test-owned command observer.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="engine">The fixture's initialized relational engine.</param>
    /// <param name="interceptors">Optional observers attached only to the returned context.</param>
    /// <returns>A context owned by the caller.</returns>
    public async Task<OrderingLockContext> CreateContextAsync(
        string engine,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var source = _databases[engine]
            .CreateContext();

        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<OrderingLockContext>().ConfigureTestWarnings();

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
        options.AddInterceptors(interceptors);

        return new OrderingLockContext(options.Options);
    }
}
