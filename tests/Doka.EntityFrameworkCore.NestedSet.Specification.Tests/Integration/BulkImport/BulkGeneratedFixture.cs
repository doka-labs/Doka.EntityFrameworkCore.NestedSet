namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns generated-key, generated-sort and immediate-FK import tables on each real engine.</summary>
public sealed class BulkGeneratedFixture : IAsyncLifetime
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

    /// <summary>Clears the import mapping and returns an independent context with optional command observers.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    public async Task<BulkGeneratedContext> ResetAsync(
        string engine,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    )
    {
        var create = !_databases.TryGetValue(engine, out var database);

        if (create)
        {
            database = await TestDatabase.CreateAsync(engine, cancellationToken: cancellationToken);
            _databases.Add(engine, database);
        }

        await using var source = database!.CreateContext();
        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<BulkGeneratedContext>()
            .ConfigureTestWarnings();

        switch (engine)
        {
            case "Sqlite":
                options.UseSqlite(connection, provider => provider
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
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        FunctionalTestContextPolicy.ConfigureOptions(options);

        await using (var setup = new BulkGeneratedContext(options.Options))
        {
            if (create)
            {
                await setup
                    .GetService<IRelationalDatabaseCreator>()
                    .CreateTablesAsync(cancellationToken);

                if (engine == "Sqlite")
                {
                    // WHY: The trigger changes a generated token after each direct parent or coordinate update.
                    await setup.Database.ExecuteSqlRawAsync(
                        "CREATE TRIGGER BulkVersion AFTER UPDATE ON BulkGeneratedNodes "
                        + "WHEN NEW.Version = OLD.Version BEGIN UPDATE BulkGeneratedNodes "
                        + "SET Version = OLD.Version + 1, Details_Revision = OLD.Details_Revision + 1 "
                        + "WHERE Id = NEW.Id; END",
                        cancellationToken);
                }
            }

            await setup
                .Set<BulkGeneratedNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    cancellationToken);

            await setup
                .Set<BulkGeneratedNode>()
                .ExecuteDeleteAsync(cancellationToken);

            await setup
                .Set<BulkManualNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    cancellationToken);

            await setup
                .Set<BulkManualNode>()
                .ExecuteDeleteAsync(cancellationToken);

            await setup
                .Set<BulkNavigationNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    cancellationToken);

            await setup
                .Set<BulkNavigationNode>()
                .ExecuteDeleteAsync(cancellationToken);

            // WHY: Fresh import cases must not inherit active identities or tombstones from a previous case.
            await NestedSetTestInfrastructure.ClearRegistriesAsync(setup, cancellationToken);
        }

        return new BulkGeneratedContext(
            options.AddInterceptors(interceptors)
                .Options);
    }
}
