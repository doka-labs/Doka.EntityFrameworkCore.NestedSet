using System.Runtime.ExceptionServices;
using Doka.EntityFrameworkCore.MySql;
using Microsoft.Extensions.DependencyInjection;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Owns real, unopened provider services for consumer construction measurements.</summary>
/// <typeparam name="TContext">The actual sample model configured by the provider.</typeparam>
internal sealed class ConsumerMetadataServices<TContext> : IDisposable, IAsyncDisposable
    where TContext : DbContext
{
    private readonly ServiceProvider _provider;

    /// <summary>Binds the finalized options to their invocation-owned provider.</summary>
    private ConsumerMetadataServices(
        ServiceProvider provider,
        DbContextOptions<TContext> options
    )
    {
        _provider = provider;
        Options = new DbContextOptionsBuilder<TContext>(options).UseInternalServiceProvider(provider).Options;
    }

    /// <summary>Gets the configured actual provider options without opening a connection.</summary>
    internal DbContextOptions<TContext> Options { get; }

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    /// <summary>Releases the consumer before its provider and preserves both cleanup failures.</summary>
    /// <param name="context">The consumer whose scoped services borrow the owned provider.</param>
    /// <returns>Completion after both ownership boundaries have been released.</returns>
    internal async ValueTask DisposeContextAsync(
        DbContext? context
    )
    {
        Exception? failure = null;
        try
        {
            if (context is not null)
            {
                await context.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            // WHY: A context cleanup failure must not skip the separately owned singleton provider graph.
            await _provider.DisposeAsync();
        }
        catch (Exception cleanup)
        {
            if (failure is not null)
            {
                throw new AggregateException(failure, cleanup);
            }

            throw;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Configures one explicit dialect without global provider-cache ownership or server discovery.</summary>
    /// <param name="engine">The qualified engine name.</param>
    /// <returns>The owner disposed after its consumer context.</returns>
    internal static ConsumerMetadataServices<TContext> Create(
        string engine
    )
    {
        var options = new DbContextOptionsBuilder<TContext>();
        switch (engine)
        {
            case "MySql":
            case "MariaDb":
                options.UseMySql(
                    "Server=127.0.0.1;Database=consumer_construction;User ID=unused;Pooling=false",
                    engine == "MySql" ? DatabaseTestTargets.MySql : DatabaseTestTargets.MariaDb,
                    provider => provider.UseNestedSets());

                break;
            case "PostgreSql":
                options.UseNpgsql(
                    "Host=127.0.0.1;Database=consumer_construction;Username=unused;Pooling=false",
                    provider => provider.UseNestedSets());

                break;
            case "SqlServer":
                options.UseSqlServer(
                    "Server=127.0.0.1;Database=consumer_construction;Integrated Security=true",
                    provider => provider.UseNestedSets());

                break;
            case "Sqlite":
                options.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine), engine, "Select a qualified engine.");
        }

        var services = new ServiceCollection();

        // WHY: The benchmark's dialect variants own their singleton graphs; unrelated discovery must not
        // add these deliberate configurations to EF's application-oriented process-global provider cache.
        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        return new ConsumerMetadataServices<TContext>(services.BuildServiceProvider(), options.Options);
    }
}
