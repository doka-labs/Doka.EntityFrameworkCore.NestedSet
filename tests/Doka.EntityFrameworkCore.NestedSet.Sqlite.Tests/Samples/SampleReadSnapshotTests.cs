extern alias SnapshotSample;

using Microsoft.Data.Sqlite;
using SampleReadSnapshot = SnapshotSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Checks the actual shared sample's native SQLite snapshot and concurrent writer contract.</summary>
public sealed class SampleReadSnapshotTests
    : ProviderTest, IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    /// <summary>Uses immutable SQLite ownership while each test owns its local database.</summary>
    /// <param name="fixture">The fixture identifying this suite's SQLite engine.</param>
    public SampleReadSnapshotTests(ProviderFixture<ProviderResources, SqliteEngine> fixture) : base(fixture) { }

    /// <summary>A WAL writer commits between reads without changing the owned reader's established snapshot.</summary>
    /// <param name="alreadyOpen">Whether the caller already owns an open reader connection.</param>
    /// <returns>A task completing after both physical connections and snapshot results have been checked.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedWalSnapshotPermitsWriterCommitAndRetainsEarlierView(bool alreadyOpen)
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE SnapshotReadSentinels (Value TEXT NOT NULL); "
            + "INSERT INTO SnapshotReadSentinels VALUES ('before');", CancellationToken.None);

        // WHY: WAL is deployment/test setup, never a per-report mutation. It allows the writer's commit
        // while the reader retains its old version, exposing an accidental BEGIN IMMEDIATE reservation.
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL", CancellationToken.None);
        await using var writer = database.CreateContext();
        ((SqliteConnection)writer.Database.GetDbConnection()).DefaultTimeout = 1;

        if (alreadyOpen)
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
        }

        var committed = false;

        // Act
        var result = await SampleReadSnapshot.RunAsync(context, async token =>
        {
            var first = await ReadValueAsync(context, token);
            await using var transaction = await writer.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, token);

            await writer.Database.ExecuteSqlRawAsync("UPDATE SnapshotReadSentinels SET Value = 'after'", token);
            await transaction.CommitAsync(token);
            committed = true;
            var second = await ReadValueAsync(context, token);

            return (First: first, Second: second);
        }, CancellationToken.None);

        var connectionState = context.Database.GetDbConnection().State;
        var latest = await ReadValueAsync(writer, CancellationToken.None);

        if (alreadyOpen)
        {
            await context.Database.CloseConnectionAsync();
        }

        // Assert
        Assert.True(committed);
        Assert.Equal(("before", "before"), result);
        Assert.Equal("after", latest);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, connectionState);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Reads one scalar through EF without tracking or depending on a sample domain model.</summary>
    private static Task<string> ReadValueAsync(DbContext context, CancellationToken cancellationToken)
        => context.Database.SqlQueryRaw<string>("SELECT Value FROM SnapshotReadSentinels")
            .SingleAsync(cancellationToken);
}
