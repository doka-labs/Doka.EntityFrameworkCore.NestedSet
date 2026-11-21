using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks cancellation-safe owned table cleanup and preservation of independent failures.</summary>
public sealed class NativeKeyCleanupTests
{
    /// <summary>Cancellation of forward work cannot prevent either owned table from being removed.</summary>
    /// <param name="partialSchema">Whether cancellation interrupted setup after only the first table existed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DropOwnedTablesAsync_CanceledForwardWork_RemovesEveryOwnedTable(
        bool partialSchema
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var probe = new DropProbe();
        await using var context = CreateContext(connection, probe);
        if (partialSchema)
        {
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE CleanupFirst (Id INTEGER NOT NULL PRIMARY KEY)",
                TestContext.Current.CancellationToken);
        }
        else
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var original = new OperationCanceledException(cancellation.Token);

        // Act
        await QueryPlanTestSupport.DropOwnedTablesAsync(context, original);
        var remaining = await CountOwnedTablesAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, remaining);
        Assert.Equal(2, probe.Attempts);
        Assert.False(probe.Cancelable);
        Assert.False(probe.HasCommandLimit);
    }

    /// <summary>One failing DROP does not abandon another table or replace an earlier work failure.</summary>
    /// <param name="workFailure">The absent, ordinary or canceled operation failure preceding cleanup.</param>
    [Theory]
    [InlineData("none")]
    [InlineData("failure")]
    [InlineData("canceled")]
    public async Task DropOwnedTablesAsync_OneDropFails_AttemptsAllTablesAndRetainsFailures(
        string workFailure
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var cleanupFailure = new IOException("Injected first DROP failure.");
        var probe = new DropProbe { Failure = cleanupFailure };
        await using var context = CreateContext(connection, probe);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        Exception? original = workFailure switch
        {
            "failure" => new InvalidOperationException("Original failed comparison."),
            "canceled" => new OperationCanceledException(new CancellationToken(canceled: true)),
            _ => null,
        };

        // Act
        var actual = await Record.ExceptionAsync(() => QueryPlanTestSupport.DropOwnedTablesAsync(context, original));
        var remaining = await CountOwnedTablesAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, probe.Attempts);
        Assert.Equal(1, remaining);
        Assert.False(probe.Cancelable);
        Assert.False(probe.HasCommandLimit);

        if (original is null)
        {
            Assert.Same(cleanupFailure, actual);
        }
        else
        {
            Assert.Collection(
                Assert.IsType<AggregateException>(actual).InnerExceptions,
                failure => Assert.Same(original, failure),
                failure => Assert.Same(cleanupFailure, failure));
        }
    }

    /// <summary>Builds a small owned schema without introducing a provider fixture or shared state.</summary>
    private static CleanupContext CreateContext(
        SqliteConnection connection,
        DropProbe probe
    )
    {
        var context = new CleanupContext(
            new DbContextOptionsBuilder<CleanupContext>().UseSqlite(connection).AddInterceptors(probe).Options);

        FunctionalTestContextPolicy.Configure(context);

        return context;
    }

    /// <summary>Counts actual remaining owned tables independently of the cleanup command observations.</summary>
    private static Task<int> CountOwnedTablesAsync(
        DbContext context,
        CancellationToken cancellationToken
    ) => context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' "
            + "AND name IN ('CleanupFirst', 'CleanupSecond')")
        .SingleAsync(cancellationToken);

    /// <summary>Contains exactly the two independent tables owned by this cleanup scenario.</summary>
    private sealed class CleanupContext : DbContext
    {
        /// <summary>Uses the connection owned by the surrounding test.</summary>
        internal CleanupContext(
            DbContextOptions<CleanupContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            string[] tables = ["CleanupFirst", "CleanupSecond"];

            foreach (var table in tables)
            {
                modelBuilder.SharedTypeEntity<Dictionary<string, object>>(table, entity =>
                {
                    entity.ToTable(table);
                    entity.Property<int>("Id").ValueGeneratedNever();
                    entity.HasKey("Id");
                });
            }
        }
    }

    /// <summary>Observes actual DROP commands and can fail one without intercepting schema setup.</summary>
    private sealed class DropProbe : DbCommandInterceptor
    {
        /// <summary>Gets the optional first-DROP failure.</summary>
        internal Exception? Failure { get; init; }

        /// <summary>Gets the number of independent owned DROP attempts.</summary>
        internal int Attempts { get; private set; }

        /// <summary>Gets whether any cleanup command received an abortable token.</summary>
        internal bool Cancelable { get; private set; }

        /// <summary>Gets whether any cleanup command retained an implicit SQL speed limit.</summary>
        internal bool HasCommandLimit { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (command.CommandText.StartsWith("DROP TABLE", StringComparison.Ordinal))
            {
                Attempts++;
                Cancelable |= cancellationToken.CanBeCanceled;
                HasCommandLimit |= command.CommandTimeout != 0;

                if (Attempts == 1 && Failure is not null)
                {
                    throw Failure;
                }
            }

            return ValueTask.FromResult(result);
        }
    }
}
