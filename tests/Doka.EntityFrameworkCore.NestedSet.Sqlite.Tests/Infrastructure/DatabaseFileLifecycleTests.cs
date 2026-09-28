namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies SQLite file cleanup and preserves initialization failures.</summary>
public sealed class DatabaseFileLifecycleTests : ProviderTest,
    IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    /// <summary>Uses the SQLite fixture owning this suite's database resources.</summary>
    /// <param name="fixture">The provider fixture owned by this suite or its test collection.</param>
    public DatabaseFileLifecycleTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>Removes a created SQLite file while preserving the exact initialization exception.</summary>
    [Fact]
    public async Task InitializationFailureReleasesItsDatabaseFile()
    {
        // Arrange
        var failure = new InvalidOperationException("Injected fixture initialization failure.");
        string? databaseFile = null;
        var existedDuringInitialization = false;

        try
        {
            // Act
            var actual = await Record.ExceptionAsync(() => TestDatabase.CreateAsync(Engine, Initialize));

            // Assert
            Assert.Same(failure, actual);
            Assert.True(existedDuringInitialization);
            Assert.NotNull(databaseFile);
            Assert.False(File.Exists(databaseFile));
            Assert.False(File.Exists(databaseFile + "-wal"));
            Assert.False(File.Exists(databaseFile + "-shm"));
        }
        finally
        {
            if (databaseFile is not null)
            {
                File.Delete(databaseFile);
                File.Delete(databaseFile + "-wal");
                File.Delete(databaseFile + "-shm");
            }
        }

        return;

        async Task Initialize(
            TreeContext context
        )
        {
            await context.Database.EnsureCreatedAsync(CancellationToken.None);
            databaseFile = context.Database.GetDbConnection().DataSource;
            existedDuringInitialization = File.Exists(databaseFile);

            throw failure;
        }
    }

    /// <summary>Reports both failures if cleanup itself cannot delete the owned resource path.</summary>
    [Fact]
    public async Task CleanupFailurePreservesTheInitializationCause()
    {
        // Arrange
        var failure = new InvalidOperationException("Injected fixture initialization failure.");
        string? databaseFile = null;

        try
        {
            // Act
            var actual = await Record.ExceptionAsync(() => TestDatabase.CreateAsync(Engine, Initialize));

            // Assert
            var combined = Assert.IsType<AggregateException>(actual);
            Assert.Equal(2, combined.InnerExceptions.Count);
            Assert.Same(failure, combined.InnerExceptions[0]);
            Assert.True(combined.InnerExceptions[1] is IOException or UnauthorizedAccessException);
        }
        finally
        {
            if (databaseFile is not null
                && Directory.Exists(databaseFile))
            {
                Directory.Delete(databaseFile);
            }
        }

        return;

        async Task Initialize(
            TreeContext context
        )
        {
            await context.Database.EnsureCreatedAsync(CancellationToken.None);
            databaseFile = context.Database.GetDbConnection().DataSource;
            File.Delete(databaseFile);
            // WHY: A directory at the owned file path makes File.Delete fail consistently without changing permissions.
            Directory.CreateDirectory(databaseFile);

            throw failure;
        }
    }
}
