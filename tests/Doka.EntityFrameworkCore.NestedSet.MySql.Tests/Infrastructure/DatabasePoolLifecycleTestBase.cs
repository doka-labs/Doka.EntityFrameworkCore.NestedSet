using System.Data.Common;
using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Verifies exact database-pool ownership on MySQL and MariaDB test servers.</summary>
public abstract class DatabasePoolLifecycleTestBase : ProviderTest
{
    /// <summary>Uses the concrete provider fixture as the immutable engine owner.</summary>
    /// <param name="fixture">The neutral resource fixture bound to this provider suite.</param>
    protected DatabasePoolLifecycleTestBase(
        IProviderFixture<ProviderResources> fixture
    ) : base(fixture) { }

    /// <summary>Verifies completed MySQL-compatible databases release only their own pooled sessions.</summary>
    /// <param name="initializationFailure">Whether database initialization fails after opening its session.</param>
    /// <param name="deletionFailure">Whether deletion fails after reopening the owned database session.</param>
    /// <returns>A task that completes after observing exact server sessions and the surviving fixture's data.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CompletedDatabaseReleasesOnlyOwnedPooledConnections(
        bool initializationFailure,
        bool deletionFailure
    )
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var live = await TestDatabase.CreateAsync(Engine);
        await using var liveContext = live.CreateContext();
        await liveContext.AddAsync(new UnrelatedRow { Id = 701 }, cancellationToken);
        await liveContext.SaveChangesAsync(cancellationToken);
        await liveContext.Database.OpenConnectionAsync(cancellationToken);
        var activeSession = await ReadMySqlSessionIdAsync(liveContext, cancellationToken);
        long idleSession;

        await using (var idleContext = live.CreateContext())
        {
            await idleContext.Database.OpenConnectionAsync(cancellationToken);
            idleSession = await ReadMySqlSessionIdAsync(idleContext, cancellationToken);
        }

        var failure = new InvalidOperationException("Injected fixture lifetime failure.");
        long completedSession = 0;

        await using var completed = initializationFailure
            ? null
            : await TestDatabase.CreateAsync(Engine, InitializeAsync);

        // Act
        Exception? error = null;

        if (initializationFailure)
        {
            error = await Record.ExceptionAsync(() => TestDatabase.CreateAsync(
                Engine,
                async context =>
                {
                    await InitializeAsync(context);

                    throw failure;
                }));
        }
        else if (deletionFailure)
        {
            error = await Record.ExceptionAsync(async () => await completed!.DisposeAsync(async context =>
            {
                await context.Database.OpenConnectionAsync(cancellationToken);

                throw failure;
            }));
        }
        else
        {
            await completed!.DisposeAsync();
        }

        long reusedIdleSession;

        await using (var reusedContext = live.CreateContext())
        {
            // WHY: Remote session removal also lags a global pool clear. Reusing the exact foreign idle
            // session detects that ownership error independently of the server's PROCESSLIST timing.
            await reusedContext.Database.OpenConnectionAsync(cancellationToken);
            reusedIdleSession = await ReadMySqlSessionIdAsync(reusedContext, cancellationToken);
        }

        await using var command = liveContext
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT ID, COMMAND FROM information_schema.PROCESSLIST "
            + "WHERE ID IN (@completed, @active, @idle)";

        foreach (var (name, value) in new[]
                 {
                     ("@completed", completedSession), ("@active", activeSession), ("@idle", idleSession),
                 })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        var observation = Stopwatch.StartNew();
        var sessions = await ReadSessionsAsync(command, cancellationToken);

        // WHY: ClearPool sends COM_QUIT and closes the socket without waiting for the server to remove
        // its session. Observe that remote completion while keeping a real pool leak a bounded failure.
        while (sessions.ContainsKey(completedSession)
               && observation.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            sessions = await ReadSessionsAsync(command, cancellationToken);
        }

        // Assert
        Assert.Equal(idleSession, reusedIdleSession);
        Assert.False(
            sessions.ContainsKey(completedSession),
            $"Completed session {completedSession} retained: {string.Join(", ", sessions)}");
        Assert.True(sessions.ContainsKey(idleSession), $"Foreign idle session {idleSession} was cleared.");
        Assert.Equal("Query", sessions[activeSession]);
        Assert.Equal("Sleep", sessions[idleSession]);
        Assert.Equal(
            1,
            await liveContext
                .Set<UnrelatedRow>()
                .CountAsync(cancellationToken));

        if (initializationFailure || deletionFailure)
        {
            Assert.Same(failure, error);
        }
        else
        {
            Assert.Null(error);
        }

        return;

        async Task InitializeAsync(
            TreeContext context
        )
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
            await context.Database.OpenConnectionAsync(cancellationToken);
            completedSession = await ReadMySqlSessionIdAsync(context, cancellationToken);
        }
    }

    /// <summary>Observes the selected physical sessions without keeping a reader open between probes.</summary>
    /// <param name="command">The reusable query containing the three exact session identifiers.</param>
    /// <param name="cancellationToken">Cancels the remote session observation.</param>
    /// <returns>The session identifiers and their current server commands.</returns>
    private static async Task<Dictionary<long, string>> ReadSessionsAsync(
        DbCommand command,
        CancellationToken cancellationToken
    )
    {
        var sessions = new Dictionary<long, string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(reader.GetInt64(0), reader.GetString(1));
        }

        return sessions;
    }

    /// <summary>Reads the physical server session owned by an already open context connection.</summary>
    /// <param name="context">The context holding the physical session open.</param>
    /// <param name="cancellationToken">Cancels the session identifier query.</param>
    /// <returns>The server identifier of this physical connection.</returns>
    private static async Task<long> ReadMySqlSessionIdAsync(
        TreeContext context,
        CancellationToken cancellationToken
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT CONNECTION_ID()";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }
}
