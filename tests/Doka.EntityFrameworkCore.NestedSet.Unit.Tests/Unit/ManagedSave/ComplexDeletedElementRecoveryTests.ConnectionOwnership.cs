namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexDeletedElementRecoveryTests
{
    /// <summary>Caller cancellation cannot prevent cleanup of the factory-owned connection.</summary>
    /// <param name="cleanupFails">Whether disposal itself also fails after releasing the connection.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledInitializationAlwaysDisposesOwnedConnection(
        bool cleanupFails
    )
    {
        // Arrange
        var cleanupFailure = new ConnectionCleanupFailure();
        await using var connection = new ObservedConnection(
            "Data Source=:memory:",
            cleanupFails
                ? cleanupFailure
                : null);

        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var seed = NewPayload(1, NewOuter("Stored", 0, 0));

        // Act
        var exception = await Record.ExceptionAsync(() => CreateAsync(seed, connection, stop.Token));

        // Assert
        var failure = exception;

        if (cleanupFails)
        {
            var aggregate = Assert.IsType<AggregateException>(exception);
            Assert.Equal(2, aggregate.InnerExceptions.Count);
            Assert.Same(cleanupFailure, aggregate.InnerExceptions[1]);
            failure = aggregate.InnerExceptions[0];
        }

        Assert.Equal(
            stop.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(failure)
                .CancellationToken);

        Assert.True(connection.WasDisposed);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>Transfers an initialized connection to the returned context until that context is disposed.</summary>
    [Fact]
    public async Task InitializedStoreTransfersConnectionOwnershipToReturnedContext()
    {
        // Arrange
        await using var connection = new ObservedConnection("Data Source=:memory:");

        // Act
        var context = await CreateAsync(
            NewPayload(1, NewOuter("Stored", 0, 0)),
            connection,
            cancellationToken: TestContext.Current.CancellationToken);

        ConnectionState transferredState;
        bool disposedBeforeContext;
        int persistedRows;

        await using (context)
        {
            transferredState = connection.State;
            disposedBeforeContext = connection.WasDisposed;
            persistedRows = await context
                .Set<Payload>()
                .CountAsync(CancellationToken.None);
        }

        // Assert
        Assert.Equal(ConnectionState.Open, transferredState);
        Assert.False(disposedBeforeContext);
        Assert.Equal(1, persistedRows);
        Assert.True(connection.WasDisposed);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>Releases the opened connection when the seed violates the store's required scalar constraint.</summary>
    [Fact]
    public async Task FailedSeedDisposesConnectionBeforeOwnershipTransfer()
    {
        // Arrange
        await using var connection = new ObservedConnection("Data Source=:memory:");
        var seed = new Payload
        {
            Id = 1,
            Marker = null!,
        };

        // Act
        var exception = await Record.ExceptionAsync(() => CreateAsync(
            seed,
            connection,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        var update = Assert.IsType<DbUpdateException>(exception);
        var provider = Assert.IsType<SqliteException>(update.InnerException);
        Assert.Equal(19, provider.SqliteErrorCode);
        Assert.True(connection.WasDisposed);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>Releases a connection even when opening fails before a seeding context can be created.</summary>
    [Fact]
    public async Task FailedOpenDisposesConnectionBeforeOwnershipTransfer()
    {
        // Arrange
        var missingDirectory = Path.Combine(
            Path.GetTempPath(),
            "nestedset-missing-"
            + Guid
                .NewGuid()
                .ToString("N"));

        await using var connection = new ObservedConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(missingDirectory, "store.sqlite"),
            }.ToString());

        var seed = NewPayload(1, NewOuter("Stored", 0, 0));

        // Act
        var exception = await Record.ExceptionAsync(() => CreateAsync(
            seed,
            connection,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        var provider = Assert.IsType<SqliteException>(exception);
        Assert.Equal(14, provider.SqliteErrorCode);
        Assert.True(connection.WasDisposed);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.False(Directory.Exists(missingDirectory));
    }

    /// <summary>Retains the initiating provider error alongside a cleanup error after releasing the store.</summary>
    [Fact]
    public async Task FailedSeedRetainsProviderAndCleanupFailures()
    {
        // Arrange
        var cleanupFailure = new ConnectionCleanupFailure();
        await using var connection = new ObservedConnection("Data Source=:memory:", cleanupFailure);
        var seed = new Payload
        {
            Id = 1,
            Marker = null!,
        };

        // Act
        var exception = await Record.ExceptionAsync(() => CreateAsync(
            seed,
            connection,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        var failures = Assert.IsType<AggregateException>(exception);
        Assert.Collection(
            failures.InnerExceptions,
            failure =>
            {
                var update = Assert.IsType<DbUpdateException>(failure);
                var provider = Assert.IsType<SqliteException>(update.InnerException);
                Assert.Equal(19, provider.SqliteErrorCode);
            },
            failure => Assert.Same(cleanupFailure, failure));

        Assert.True(connection.WasDisposed);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>Observes real disposal and can fail once after the provider releases the connection.</summary>
    private sealed class ObservedConnection(
        string connectionString,
        Exception? disposalFailure = null
    )
        : SqliteConnection(connectionString)
    {
        private Exception? _disposalFailure = disposalFailure;

        /// <summary>Gets whether asynchronous disposal has been requested.</summary>
        internal bool WasDisposed { get; private set; }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            WasDisposed = true;
            var failure = _disposalFailure;
            _disposalFailure = null;
            await base.DisposeAsync();

            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    /// <summary>Distinguishes an injected cleanup failure from the initiating provider constraint failure.</summary>
    private sealed class ConnectionCleanupFailure : Exception;
}
