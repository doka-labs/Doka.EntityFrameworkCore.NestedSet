namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns ordinary provider services for tests that require controlled cache residency.</summary>
internal static class CacheTestServices
{
    /// <summary>Creates a disposable graph with the provider's normal compiler and bounded EF cache.</summary>
    /// <param name="engine">The real database engine used by the cache experiment.</param>
    /// <returns>The graph shared by warm and measured contexts and disposed after both.</returns>
    internal static ServiceProvider Create(
        string engine
    )
    {
        var services = new ServiceCollection();

        switch (engine)
        {
            case "Sqlite":
                services.AddEntityFrameworkSqlite();
                break;
            case "MySql":
            case "MariaDb":
                services.AddEntityFrameworkDokaMySql();
                break;
            case "PostgreSql":
                services.AddEntityFrameworkNpgsql();
                break;
            case "SqlServer":
                services.AddEntityFrameworkSqlServer();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        // WHY: Unrelated suite queries can evict a just-warmed shape from EF's shared model/query cache.
        // Explicit ownership isolates the experiment without changing normal cache limits or retaining global graphs.
        return services.BuildServiceProvider();
    }
}
