namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Owns an isolated SQL Server database with a generated-token hierarchy table.</summary>
public sealed class RowVersionFixture : IAsyncLifetime
{
    private TestDatabase? _database;

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Returns an empty context against fixture-owned tables.</summary>
    public async Task<RowVersionContext> ResetAsync(
        bool readCommittedSnapshot = false
    )
    {
        var initialize = _database is null;
        _database ??= await TestDatabase.CreateAsync("SqlServer");
        await using var source = _database.CreateContext();

        // WHY: Locking must protect structural reads under both locking read committed and database-level RCSI.
        var isolationSql = "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT "
            + (readCommittedSnapshot ? "ON" : "OFF")
            + " WITH ROLLBACK IMMEDIATE";

        await source.Database.ExecuteSqlRawAsync(isolationSql, CancellationToken.None);
        var options = new DbContextOptionsBuilder<RowVersionContext>()
            .ConfigureTestWarnings()
            .UseSqlServer(source.Database.GetConnectionString())
            .UseNestedSets()
            .Options;

        var context = new RowVersionContext(options);

        if (initialize)
        {
            await context
                .GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);
        }

        await context
            .Set<SqlServerVersionNode>()
            .ExecuteDeleteAsync(CancellationToken.None);

        // WHY: A complete fixture reset must also release this model's active and tombstoned tree identities.
        // Clearing only payload rows leaves Guid.Empty reserved by the preceding rowversion test case.
        await context.ClearNestedSetTreeRegistriesAsync(CancellationToken.None);

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
