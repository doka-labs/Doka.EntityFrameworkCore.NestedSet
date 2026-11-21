namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns independent ordering tables while reusing the established real-engine provisioning.</summary>
public sealed class OrderingFixture : IAsyncLifetime
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

    /// <summary>Returns an empty strict or flexible model without modifying another test fixture.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="engine">The real relational engine.</param>
    /// <param name="mode">Strict, Flexible, or Converted.</param>
    /// <returns>A context owned by the test.</returns>
    public async Task<OrderingContext> ResetAsync(
        string engine,
        string mode,
        CancellationToken cancellationToken
    )
    {
        if (!_databases.TryGetValue(engine, out var database))
        {
            database = await TestDatabase.CreateAsync(engine, cancellationToken: cancellationToken);
            _databases.Add(engine, database);

            try
            {
                foreach (var model in new[] { "Strict", "Flexible", "Converted" })
                {
                    await using var setup = await CreateContextAsync(
                        engine,
                        cancellationToken: cancellationToken,
                        mode: model);

                    await setup
                        .GetService<IRelationalDatabaseCreator>()
                        .CreateTablesAsync(cancellationToken);
                }
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

        var context = await CreateContextAsync(engine, cancellationToken: cancellationToken, mode: mode);

        try
        {
            await context
                .Set<OrderingNode>()
                .Where(node => node.ParentId != null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    cancellationToken);

            // WHY: The production model intentionally uses a restrictive self-FK. Unlinking the fixture rows first
            // keeps
            // cleanup valid after an interrupted mutation leaves a populated hierarchy behind.
            await context
                .Set<OrderingNode>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingMarker>()
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

    /// <summary>Creates an independent context with optional fault observers against an initialized model.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="engine">The real relational engine.</param>
    /// <param name="mode">The mapped ordering contract.</param>
    /// <param name="interceptors">Observers owned by the calling test.</param>
    /// <returns>A context owned by the caller.</returns>
    public async Task<OrderingContext> CreateContextAsync(
        string engine,
        string mode,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var source = _databases[engine]
            .CreateContext();

        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder().ConfigureTestWarnings();

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
        var observers = interceptors
            .OfType<OrderingSaveProbe>()
            .ToArray();

        if (observers.Length != 0)
        {
            // WHY: Only actual entity-materialization assertions need the singleton observer and its service graph.
            options.AddInterceptors(TestMaterializationObserver.Instance);
        }

        OrderingContext context = mode switch
        {
            "Strict" => new StrictOrderingContext(options.Options),
            "Flexible" => new FlexibleOrderingContext(options.Options),
            "Converted" => new ConvertedOrderingContext(options.Options),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        foreach (var observer in observers)
        {
            TestMaterializationObserver.Instance.Register(context, observer.RecordMaterialization);
        }

        return context;
    }
}
