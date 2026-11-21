using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>
    /// Runs one actual sample context in the provider's existing database without duplicating schema setup.
    /// </summary>
    private Task<SignalLabContext> CreateContextAsync(
        SignalReportProbe probe,
        CancellationToken cancellationToken
    )
        => CreateContextAsync<SignalLabContext>(
            _fixture,
            static options => new SignalLabContext(options),
            probe,
            cancellationToken: cancellationToken);

    /// <summary>Creates every sample model with the same observation and snapshot prerequisites.</summary>
    /// <typeparam name="TContext">The normal or indexed sample context.</typeparam>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="database">The shared or case-owned fixture database.</param>
    /// <param name="createContext">Creates the requested context using the fixture's provider options.</param>
    /// <param name="probe">The context-specific materialization and command observer.</param>
    /// <returns>The context with live entity observation and the required SQL Server snapshot option.</returns>
    private async Task<TContext> CreateContextAsync<TContext>(
        ModelCompatibilityDatabase database,
        Func<DbContextOptions<TContext>, TContext> createContext,
        SignalReportProbe probe,
        CancellationToken cancellationToken
    )
        where TContext : SignalLabContext
    {
        var context = await database.CreateContextAsync(
            Engine,
            createContext,
            cancellationToken: cancellationToken,
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, probe.Materialized);

        if (Engine == "SqlServer")
        {
            // WHY: Snapshot is an application database prerequisite, explicitly enabled by this owned test setup only.
            await context.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON",
                cancellationToken);
        }

        return context;
    }
}
