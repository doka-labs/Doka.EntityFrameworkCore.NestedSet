using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Owns an isolated SQL Server database with a generated-token hierarchy table.</summary>
public sealed class RowVersionFixture : IAsyncLifetime
{
    private TestDatabase? _database;

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Returns an empty context against fixture-owned tables.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="readCommittedSnapshot">Whether SQL Server read-committed snapshot isolation is enabled.</param>
    public async Task<RowVersionContext> ResetAsync(
        CancellationToken cancellationToken,
        bool readCommittedSnapshot = false
    )
    {
        var initialize = _database is null;
        _database ??= await TestDatabase.CreateAsync("SqlServer", cancellationToken: cancellationToken);
        await using var source = _database.CreateContext();

        // WHY: Locking must protect structural reads under both locking read committed and database-level RCSI.
        var isolationSql = "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT "
            + (readCommittedSnapshot ? "ON" : "OFF")
            + " WITH ROLLBACK IMMEDIATE";

        await source.Database.ExecuteSqlRawAsync(isolationSql, cancellationToken);
        var options = new DbContextOptionsBuilder<RowVersionContext>()
            .ConfigureTestWarnings()
            .UseSqlServer(source.Database.GetConnectionString(), provider => provider.UseNestedSets())
            .Options;

        var context = new RowVersionContext(options);

        if (initialize)
        {
            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);
        }

        await context
            .Set<SqlServerVersionNode>()
            .ExecuteDeleteAsync(cancellationToken);

        // WHY: A complete fixture reset must also release this model's active and tombstoned tree identities.
        // Clearing only payload rows leaves Guid.Empty reserved by the preceding rowversion test case.
        await context.ClearNestedSetTreeRegistriesAsync(cancellationToken);

        return context;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }
}
