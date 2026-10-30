extern alias UserGroupsSample;

using Microsoft.EntityFrameworkCore.Storage;
using SampleReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SampleReadSnapshotTests
{
    /// <summary>A connection without EF's separate enlistment contract is rejected before acquiring resources.</summary>
    /// <param name="alreadyOpen">Whether the application already owns the physical SQLite connection.</param>
    /// <returns>A task completing after proving the unsupported seam performs no reads, opens, or native begins.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingEnlistmentContractRejectsBeforeOpeningConnection(bool alreadyOpen)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        if (alreadyOpen)
        {
            await connection.OpenAsync(CancellationToken.None);
        }

        // WHY: A RelationalConnection subclass always implements the missing interface. An interface-only
        // proxy represents the supported service-replacement seam without accidentally providing that contract.
        var replacement = DispatchProxy.Create<IRelationalConnection, SnapshotInterfaceConnection>();
        var probe = (SnapshotInterfaceConnection)replacement;
        probe.Connection = connection;
        var options = new DbContextOptionsBuilder<SampleLifecycleContext>().UseSqlite(connection);
        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        services.AddScoped(_ => replacement);
        await using var provider = services.BuildServiceProvider();
        await using var context = new SampleLifecycleContext(options.UseInternalServiceProvider(provider).Options);
        var reads = 0;

        // Act
        var error = await Record.ExceptionAsync(() => SampleReadSnapshot.RunAsync(context, _ =>
        {
            reads++;

            return Task.FromResult(42);
        }, CancellationToken.None));

        var state = connection.State;

        // Assert
        Assert.IsNotAssignableFrom<ITransactionEnlistmentManager>(replacement);
        Assert.Equal("The SQLite read snapshot requires a relational connection with the EF enlistment contract.",
            Assert.IsType<NotSupportedException>(error).Message);

        Assert.Equal(0, reads);
        Assert.Equal(0, probe.ResourceCalls);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, state);
    }

    /// <summary>Exposes only IRelationalConnection so an unsupported service cannot pass by inheritance.</summary>
    // WHY: DispatchProxy creates the concrete subtype in its separate generated assembly. Expose that
    // inheritance boundary explicitly, as with the existing metadata probes; this test assembly is not shipped.
    public class SnapshotInterfaceConnection : DispatchProxy
    {
        /// <summary>Allows DispatchProxy to create the interface-only test service before its connection is assigned.</summary>
        public SnapshotInterfaceConnection() { }

        /// <summary>Gets or sets the application-owned connection exposed by the metadata-only service.</summary>
        internal SqliteConnection? Connection { get; set; }

        /// <summary>Gets the number of attempted resource operations rather than harmless metadata requests.</summary>
        internal int ResourceCalls { get; private set; }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_DbConnection":
                    return Connection;
                case "get_CurrentTransaction":
                    return null;
                case nameof(IDisposable.Dispose):
                case nameof(IRelationalConnection.ResetState):
                    return null;
                case nameof(IAsyncDisposable.DisposeAsync):
                    return ValueTask.CompletedTask;
                case nameof(IRelationalConnection.ResetStateAsync):
                    return Task.CompletedTask;
                default:
                    ResourceCalls++;

                    throw new InvalidOperationException(
                        $"An unsupported snapshot connection was used through '{targetMethod?.Name}'.");
            }
        }
    }
}
