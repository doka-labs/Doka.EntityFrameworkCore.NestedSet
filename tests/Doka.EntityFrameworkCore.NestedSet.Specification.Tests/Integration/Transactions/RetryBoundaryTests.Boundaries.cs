using System.Transactions;
using DatabaseIsolationLevel = System.Data.IsolationLevel;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RetryBoundaryTests
{
    /// <summary>Rejects an ambient coordinated save while preserving application-owned pending values.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChangesAsync_OrderedSave_RejectsAmbientTransactionsBeforeSql(
        bool acceptChanges
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var commands = new EnterpriseProbe();
        var transactions = new RetryBoundaryTransactionProbe();
        await using var context = Create(setup, null, commands, transactions);
        var renamed = await context
            .Set<OrderingNode>()
            .SingleAsync(row => row.Id == 1, CancellationToken.None);

        renamed.Name = "Zulu";
        commands.Commands.Clear();
        Exception? failure;

        // Act
        using (var ambient = new TransactionScope(
            TransactionScopeAsyncFlowOption.Enabled))
        {
            failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(
                acceptChanges,
                CancellationToken.None));
        }

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidTransaction,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Empty(commands.Commands);
        Assert.Equal(0, transactions.Starts);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(renamed)
                .State);

        Assert.Equal("Zulu", renamed.Name);
        Assert.Equal(
            "Alpha",
            await setup
                .Set<OrderingNode>()
                .Where(row => row.Id == 1)
                .Select(row => row.Name)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Rejects a configured retry policy when its caller transaction lives outside a retry delegate.</summary>
    [Fact]
    public async Task InsertRootAsync_CallerTransaction_RequiresRetryStrategyBoundary()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await using var services = RetryBoundaryServices.Create(Engine);
        var commands = new EnterpriseProbe();
        await using var context = Create(setup, services, commands);
        await using var transaction = await context.Database.BeginTransactionAsync(
            Engine == "Sqlite"
                ? DatabaseIsolationLevel.Serializable
                : DatabaseIsolationLevel.ReadCommitted,
            CancellationToken.None);

        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        commands.Commands.Clear();

        // Act
        var failure = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Alpha",
            },
            Guid.Empty,
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidTransaction,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Empty(commands.Commands);
        Assert.Empty(
            await setup
                .Set<OrderingNode>()
                .ToArrayAsync(CancellationToken.None));
    }
}
