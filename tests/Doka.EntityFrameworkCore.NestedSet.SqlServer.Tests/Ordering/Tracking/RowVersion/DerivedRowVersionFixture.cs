using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Owns independent SQL Server tables for mixed-subtype generated-token refresh cases.</summary>
public sealed class DerivedRowVersionFixture : IAsyncLifetime
{
    private TestDatabase? _database;

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Resets both hierarchy rows and reserved tree identities before one independent probe.</summary>
    /// <param name="interceptor">An optional scalar-refresh observation or failure probe.</param>
    public async Task<DerivedRowVersionContext> ResetAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor? interceptor = null
    )
    {
        var initialize = _database is null;
        _database ??= await TestDatabase.CreateAsync("SqlServer");
        await using var source = _database.CreateContext();
        var builder = new DbContextOptionsBuilder<DerivedRowVersionContext>()
            .ConfigureTestWarnings()
            .UseSqlServer(source.Database.GetConnectionString())
            .UseNestedSets();

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        var context = new DerivedRowVersionContext(builder.Options);

        if (initialize)
        {
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(CancellationToken.None);
        }

        await context.Set<DerivedVersionBaseNode>().ExecuteDeleteAsync(CancellationToken.None);
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
