extern alias UserGroupsSample;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using AmbientTransaction = System.Transactions.Transaction;
using CommittableTransaction = System.Transactions.CommittableTransaction;
using SampleReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SampleReadSnapshotTests
{
    /// <summary>A customized connection's own adoption preconditions preserve the caller's open reference.</summary>
    /// <param name="boundary">The connection-local ambient or enlisted boundary introduced at native begin.</param>
    /// <param name="alreadyOpen">Whether the caller already owns an EF connection-open reference.</param>
    /// <returns>A task completing after native disposal and exact caller ownership have been checked.</returns>
    [Theory]
    [InlineData("none", false)]
    [InlineData("none", true)]
    [InlineData("ambient", false)]
    [InlineData("ambient", true)]
    [InlineData("enlisted", false)]
    [InlineData("enlisted", true)]
    public async Task CustomEnlistmentBoundaryPreservesConnectionOwnership(
        string boundary,
        bool alreadyOpen
    )
    {
        // Arrange
        using var enlistment = new CommittableTransaction();
        await using var connection = new SnapshotEnlistmentConnection(boundary, enlistment);
        var options = new DbContextOptionsBuilder<SampleLifecycleContext>().UseSqlite(connection);
        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: Own this deliberate service replacement instead of adding its graph to EF's global cache.
        services.AddScoped<IRelationalConnection, SnapshotEnlistmentRelationalConnection>();
        await using var provider = services.BuildServiceProvider();
        await using var context = new SampleLifecycleContext(options.UseInternalServiceProvider(provider).Options);

        if (alreadyOpen)
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
        }

        var reads = 0;
        var result = 0;
        IsolationLevel? isolation = null;

        // Act
        var error = await Record.ExceptionAsync(async () =>
        {
            result = await SampleReadSnapshot.RunAsync(context, _ =>
            {
                reads++;
                isolation = context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel;

                return Task.FromResult(42);
            }, CancellationToken.None);
        });

        var state = connection.State;
        var nativeConnection = connection.Begun?.Connection;

        if (alreadyOpen)
        {
            await context.Database.CloseConnectionAsync();
        }

        // Assert
        if (boundary == "none")
        {
            Assert.Null(error);
            Assert.Equal(42, result);
            Assert.Equal(1, reads);
            Assert.Equal(IsolationLevel.Serializable, isolation);
        }
        else
        {
            Assert.Equal("The SQLite read snapshot cannot attach inside an ambient or enlisted transaction.",
                Assert.IsType<InvalidOperationException>(error).Message);

            Assert.Equal(0, reads);
            Assert.Null(isolation);
        }

        Assert.Null(AmbientTransaction.Current);
        Assert.NotNull(connection.Begun);
        Assert.Null(nativeConnection);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, state);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>Introduces a provider-owned boundary without changing the global ambient transaction.</summary>
    private sealed class SnapshotEnlistmentConnection(string boundary, CommittableTransaction enlistment)
        : SqliteConnection("Data Source=:memory:")
    {
        /// <summary>Gets the native transaction that must be disposed even when EF rejects adoption.</summary>
        internal SqliteTransaction? Begun { get; private set; }

        /// <summary>Gets the customized ambient precondition after the native transaction has begun.</summary>
        internal AmbientTransaction? Ambient => Begun is not null && boundary == "ambient" ? enlistment : null;

        /// <summary>Gets the customized enlisted precondition after the native transaction has begun.</summary>
        internal AmbientTransaction? Enlisted => Begun is not null && boundary == "enlisted" ? enlistment : null;

        /// <inheritdoc />
        public override SqliteTransaction BeginTransaction(
            IsolationLevel isolationLevel,
            bool deferred
        )
        {
            Begun = base.BeginTransaction(isolationLevel, deferred);

            return Begun;
        }
    }

    /// <summary>Exercises the public relational extension seam with connection-specific transaction state.</summary>
    /// <param name="dependencies">The configured context's relational dependencies.</param>
    private sealed class SnapshotEnlistmentRelationalConnection(RelationalConnectionDependencies dependencies)
        : RelationalConnection(dependencies)
    {
        /// <inheritdoc />
        public override AmbientTransaction? CurrentAmbientTransaction
            => ((SnapshotEnlistmentConnection)DbConnection).Ambient;

        /// <inheritdoc />
        public override AmbientTransaction? EnlistedTransaction
        {
            get => ((SnapshotEnlistmentConnection)DbConnection).Enlisted;
            protected set => base.EnlistedTransaction = value;
        }

        /// <inheritdoc />
        protected override DbConnection CreateDbConnection() => new SqliteConnection(GetValidatedConnectionString());
    }
}
