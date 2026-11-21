using Microsoft.Extensions.DependencyInjection;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Owns provider services and an exact shared fixture context outside measured operations.</summary>
/// <typeparam name="TContext">The actual fixture model used by the former allocation check.</typeparam>
internal sealed class ManagedSaveContextOwner<TContext> : IDisposable
    where TContext : DbContext
{
    private readonly ServiceProvider _services;

    /// <summary>Retains ownership in the order required for context-before-provider cleanup.</summary>
    private ManagedSaveContextOwner(
        ServiceProvider services,
        TContext context
    )
    {
        _services = services;
        Context = context;
    }

    /// <summary>Gets the independently configured model and tracker used by the workload.</summary>
    internal TContext Context { get; }

    /// <summary>Builds the real SQLite services without populating EF's process-global provider cache.</summary>
    /// <param name="create">Constructs the exact fixture context from its owned options.</param>
    /// <param name="hierarchy">Whether this fixture uses the real NestedSet provider registration.</param>
    /// <returns>The context and provider lifetime owner.</returns>
    internal static ManagedSaveContextOwner<TContext> Create(
        Func<DbContextOptions<TContext>, TContext> create,
        bool hierarchy
    )
    {
        var builder = new DbContextOptionsBuilder<TContext>();

        if (hierarchy)
        {
            builder.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());
        }
        else
        {
            builder.UseSqlite("Data Source=:memory:");
        }

        var registrations = new ServiceCollection();

        // WHY: Apply the fixture's real extensions once; an owned graph preserves warning behavior without
        // accumulating deliberate benchmark model variants in EF's application-oriented global cache.
        foreach (var extension in builder.Options.Extensions)
        {
            extension.ApplyServices(registrations);
        }

        var services = registrations.BuildServiceProvider();

        try
        {
            return new ManagedSaveContextOwner<TContext>(
                services,
                create(builder.UseInternalServiceProvider(services).Options));
        }
        catch
        {
            services.Dispose();

            throw;
        }
    }

    /// <inheritdoc />
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
