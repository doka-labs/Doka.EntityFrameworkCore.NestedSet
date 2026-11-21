using Microsoft.Extensions.DependencyInjection;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Owns disconnected benchmark provider services without retaining variants in EF's global cache.</summary>
/// <typeparam name="TContext">The exact context type whose model supplies the measured metadata.</typeparam>
internal abstract class TrackingMeasurementContext<TContext> : DbContext
    where TContext : DbContext
{
    private readonly ServiceProvider _services;

    /// <summary>Installs the real provider and NestedSet conventions before EF constructs the context.</summary>
    protected TrackingMeasurementContext() : this(CreateServices()) { }

    /// <summary>Retains the exact service owner paired with these context options.</summary>
    private TrackingMeasurementContext(
        (ServiceProvider Provider, DbContextOptions<TContext> Options) owner
    ) : base(owner.Options)
    {
        _services = owner.Provider;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        try
        {
            base.Dispose();
        }
        finally
        {
            _services.Dispose();
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            await _services.DisposeAsync();
        }
    }

    /// <summary>Creates one explicit provider owner for the context's actual extension registrations.</summary>
    private static (ServiceProvider Provider, DbContextOptions<TContext> Options) CreateServices()
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;

        var services = new ServiceCollection();

        // WHY: Each benchmark fixture intentionally finalizes its own model; benchmark lifetime owns its services.
        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        var provider = services.BuildServiceProvider();
        var owned = new DbContextOptionsBuilder<TContext>(options).UseInternalServiceProvider(provider).Options;

        return (provider, owned);
    }
}
