namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Prepares actual sample contexts within the existing launcher-owned database.</summary>
internal static class ConsumerBenchmarkDatabase
{
    /// <summary>Uses the real selected provider without registering untimed observers during timing runs.</summary>
    internal static DbContextOptions Options(
        CommandObservation observation
    )
    {
        var options = BenchmarkRunOptions.Current;
        var builder = new DbContextOptionsBuilder();
        BenchmarkEnvironment.ConfigureProvider(builder, options.Engine, BenchmarkEnvironment.ConnectionString);

        if (options.Diagnostics)
        {
            builder.AddInterceptors(observation);
        }

        return builder.Options;
    }

    /// <summary>Creates this iteration's actual sample schema before any measured reader starts.</summary>
    /// <param name="context">The context used for the benchmark data and verification reads.</param>
    /// <param name="cancellationToken">Cancellation for the operation's preparation or verification work.</param>
    internal static async Task ResetAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await context.Database.CloseConnectionAsync();
        await context.Database.EnsureDeletedAsync(cancellationToken);

        if (BenchmarkRunOptions.Current.Engine == BenchmarkEngine.SqliteMemory)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        await context.Database.EnsureCreatedAsync(cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);

        if (context.Database.IsSqlServer())
        {
            // WHY: The actual multi-reader sample requires a shared snapshot. This is explicit benchmark setup
            // on the launcher-owned database, never an automatic library or production-reader ALTER DATABASE.
            await context.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON",
                cancellationToken);
        }
    }
}
