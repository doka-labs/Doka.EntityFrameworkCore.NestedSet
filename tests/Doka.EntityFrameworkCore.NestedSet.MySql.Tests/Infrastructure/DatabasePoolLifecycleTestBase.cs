using System.Data.Common;
using System.Globalization;
using MySqlConnector;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Verifies exact database-pool ownership on MySQL and MariaDB test servers.</summary>
public abstract class DatabasePoolLifecycleTestBase : ProviderTest
{
    /// <summary>A changed native timeout cannot leave a reusable session in a disposed fixture's second pool.</summary>
    /// <returns>A task that completes after a fresh session proves the exact former pool was cleared.</returns>
    [Fact]
    public async Task DisposeAsync_DerivedPoolAndDeletionFailure_DoesNotReuseCompletedSession()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        long completedSession = 0;
        var poolString = string.Empty;
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            async (context, initializationCancellationToken) =>
            {
                var connection = context.Database.GetDbConnection();
                var options = new MySqlConnectionStringBuilder(connection.ConnectionString)
                {
                    DefaultCommandTimeout = 0,
                    ApplicationName = "NestedSet derived functional pool",
                };

                connection.ConnectionString = options.ConnectionString;
                poolString = options.ConnectionString;
                await context.Database.EnsureCreatedAsync(initializationCancellationToken);
                await context.Database.OpenConnectionAsync(initializationCancellationToken);
                completedSession = await ReadMySqlSessionIdAsync(context, initializationCancellationToken);
            },
            cancellationToken);

        var expected = new InvalidOperationException("Injected database deletion failure.");

        // Act
        var actual = await Record.ExceptionAsync(async () => await database.DisposeAsync(
            _ => Task.FromException(expected)));

        long reusedSession;
        await using (var probe = new MySqlConnection(poolString))
        {
            try
            {
                await probe.OpenAsync(cancellationToken);
                reusedSession = await ReadMySqlSessionIdAsync(probe, cancellationToken);
            }
            finally
            {
                // WHY: The adversarial read owns this new connection and must release it even on assertion failure.
                await probe.CloseAsync();
                await MySqlConnection.ClearPoolAsync(probe, CancellationToken.None);
            }
        }

        // Assert
        Assert.Same(expected, actual);
        Assert.NotEqual(0, completedSession);
        Assert.NotEqual(completedSession, reusedSession);
    }

    /// <summary>Uses the concrete provider fixture as the immutable engine owner.</summary>
    /// <param name="fixture">The neutral resource fixture bound to this provider suite.</param>
    protected DatabasePoolLifecycleTestBase(
        IProviderFixture<ProviderResources> fixture
    ) : base(fixture) { }

    /// <summary>Verifies completed MySQL-compatible databases release only their own pooled sessions.</summary>
    /// <param name="initializationFailure">Whether database initialization fails after opening its session.</param>
    /// <param name="deletionFailure">Whether deletion fails after reopening the owned database session.</param>
    /// <param name="derivedPool">Whether initialization uses an independently keyed functional connection pool.</param>
    /// <returns>A task that completes after observing exact server sessions and the surviving fixture's data.</returns>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task CreateAsync_CompletedDatabase_ReleasesOnlyOwnedPooledConnections(
        bool initializationFailure,
        bool deletionFailure,
        bool derivedPool
    )
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var live = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

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
            : await TestDatabase.CreateAsync(
                Engine,
                InitializeAsync,
                cancellationToken: TestContext.Current.CancellationToken);

        // Act
        Exception? error = null;

        if (initializationFailure)
        {
            error = await Record.ExceptionAsync(() => TestDatabase.CreateAsync(
                Engine,
                async (context, token) =>
                {
                    await InitializeAsync(context, cancellationToken: token);

                    throw failure;
                },
                cancellationToken: TestContext.Current.CancellationToken));
        }
        else if (deletionFailure)
        {
            error = await Record.ExceptionAsync(async () => await completed!.DisposeAsync(async context =>
            {
                // WHY: The injected deletion failure belongs to protected resource cleanup.
                await context.Database.OpenConnectionAsync(CancellationToken.None);

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

        var sessions = await ReadSessionsAsync(command, cancellationToken);

        // WHY: ClearPool sends COM_QUIT and closes the socket without waiting for the server to remove
        // its session. Await that acknowledgement using only the runner token; server load is not a contract.
        while (sessions.ContainsKey(completedSession))
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
            TreeContext context,
            CancellationToken cancellationToken
        )
        {
            if (derivedPool)
            {
                var connection = context.Database.GetDbConnection();
                var options = new MySqlConnectionStringBuilder(connection.ConnectionString)
                {
                    ApplicationName = "NestedSet isolated lifecycle variant",
                };

                connection.ConnectionString = options.ConnectionString;
            }

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
    ) => await ReadMySqlSessionIdAsync(context.Database.GetDbConnection(), cancellationToken);

    /// <summary>Reads an independently owned native probe without reusing the disposed fixture object.</summary>
    private static async Task<long> ReadMySqlSessionIdAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT CONNECTION_ID()";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }
}
