namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns native case-sensitive and binary scope tables for managed import guard regressions.</summary>
public sealed class BulkStageGuardFixture : IAsyncLifetime
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

    /// <summary>Resets guard tables without restarting a shared fixture-owned database engine.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    public async Task<BulkStageGuardContext> ResetAsync(
        string engine,
        CancellationToken cancellationToken
    )
    {
        var create = !_databases.TryGetValue(engine, out var database);

        if (create)
        {
            database = await TestDatabase.CreateAsync(engine, cancellationToken: cancellationToken);
            _databases.Add(engine, database);
        }

        await using var source = database!.CreateContext();
        var extensions = source
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptions<DbContext>(extensions);
        var context = new BulkStageGuardContext(options);

        if (create)
        {
            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);
        }

        await context
            .Set<BulkStageTextNode>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (string?)null),
                cancellationToken);

        await context
            .Set<BulkStageTextNode>()
            .ExecuteDeleteAsync(cancellationToken);

        await context
            .Set<BulkStageBinaryParent>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (byte[]?)null),
                cancellationToken);

        await context
            .Set<BulkStageBinaryParent>()
            .ExecuteDeleteAsync(cancellationToken);

        await context
            .Set<BulkStageBinaryScope>()
            .ExecuteDeleteAsync(cancellationToken);

        // WHY: Fresh import cases must not inherit active identities or tombstones from a previous case.
        await NestedSetTestInfrastructure.ClearRegistriesAsync(context, cancellationToken);

        return context;
    }
}
