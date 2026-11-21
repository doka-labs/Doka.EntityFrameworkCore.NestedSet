using Doka.EntityFrameworkCore.MySql;
using Microsoft.Extensions.DependencyInjection;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Owns actual selected provider services without database I/O or global test variants.</summary>
internal sealed class ProviderMeasurementContext : IDisposable
{
    private readonly ServiceProvider _services;

    /// <summary>Selects the real provider callback and retains independently disposable services.</summary>
    internal ProviderMeasurementContext(
        string engine
    )
    {
        var builder = new DbContextOptionsBuilder<DbContext>();
        switch (engine)
        {
            case "MySql":
            case "MariaDb":
                builder.UseMySql("Server=127.0.0.1;Database=provider_observation;User ID=unused;Pooling=false",
                    engine == "MySql" ? DatabaseTestTargets.MySql : DatabaseTestTargets.MariaDb,
                    provider => provider.UseNestedSets());
                break;
            case "PostgreSql":
                builder.UseNpgsql("Host=127.0.0.1;Database=provider_observation;Username=unused;Pooling=false",
                    provider => provider.UseNestedSets());
                break;
            case "SqlServer":
                builder.UseSqlServer("Server=127.0.0.1;Database=provider_observation;Integrated Security=true",
                    provider => provider.UseNestedSets());
                break;
            case "Sqlite":
                builder.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        var services = new ServiceCollection();
        foreach (var extension in builder.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        _services = services.BuildServiceProvider();
        Context = new DbContext(builder.UseInternalServiceProvider(_services).Options);
    }

    /// <summary>Gets the actual unopened context whose adapter is selected by the provider callback.</summary>
    internal DbContext Context { get; }

    /// <summary>Releases context scopes before their provider graph.</summary>
    public void Dispose()
    {
        try
        {
            Context.Dispose();
        }
        finally
        {
            _services.Dispose();
        }
    }
}
