namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns unordered generated-key tables with computed concurrency and complex scalar values.</summary>
public sealed class BulkIntervalRefreshFixture : IAsyncLifetime
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

    /// <summary>Returns a cleared generated-value context on the selected real relational engine.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    public async Task<BulkIntervalRefreshContext> ResetAsync(
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
        var options = new DbContextOptionsBuilder<BulkIntervalRefreshContext>()
            .ConfigureTestWarnings();

        foreach (var extension in source.GetService<IDbContextOptions>()
            .Extensions)
        {
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(extension);
        }

        await using (var setup = new BulkIntervalRefreshContext(options.Options))
        {
            if (create)
            {
                await setup
                    .GetService<IRelationalDatabaseCreator>()
                    .CreateTablesAsync(cancellationToken);
            }

            await setup
                .Set<BulkIntervalNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    cancellationToken);

            await setup
                .Set<BulkIntervalNode>()
                .ExecuteDeleteAsync(cancellationToken);

            await setup
                .Set<BulkStageTextNode>()
                .ExecuteDeleteAsync(cancellationToken);

            await setup
                .Set<BulkStageBinaryParent>()
                .ExecuteDeleteAsync(cancellationToken);

            // WHY: Fresh import cases must not inherit active identities or tombstones from a previous case.
            await NestedSetTestInfrastructure.ClearRegistriesAsync(setup, cancellationToken);
        }

        return new BulkIntervalRefreshContext(
            options.AddInterceptors(interceptors)
                .Options);
    }
}
