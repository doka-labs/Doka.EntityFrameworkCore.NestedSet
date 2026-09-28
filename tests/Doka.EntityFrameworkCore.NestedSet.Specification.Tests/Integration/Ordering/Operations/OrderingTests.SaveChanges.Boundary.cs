namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Uses the outer save boundary for both same-tree and cross-tree Parent moves.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "This staged contender requires independent server row locks; SQLite serializes all database writers."
    )]
    public async Task ParentMovesDoNotReacquireRegistryLocksAfterPayload()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var hierarchy = setup.NestedSet<OrderingNode>().ForScope(1);
        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Bravo"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(4, "Charlie"), 1, CancellationToken.None);
        await hierarchy.InsertRootAsync(Node(10, "Destination"), secondTree, CancellationToken.None);
        var commands = new OrderingSaveProbe();
        var savepoints = new SavepointProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", commands, savepoints);
        var changed = await context.Set<OrderingNode>()
            .Where(node => node.Id == 2 || node.Id == 4)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        changed[0].ParentId = 3;
        changed[0].Name = "Alpha moved";
        changed[1].ParentId = 10;
        commands.Commands.Clear();

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        var payloadIndex = commands.Commands.FindIndex(command =>
            command.Contains("OrderingNodes", StringComparison.OrdinalIgnoreCase)
            && command.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
            && command.Contains("Name", StringComparison.OrdinalIgnoreCase));
        Assert.True(payloadIndex >= 0);
        Assert.Contains(commands.Commands.Take(payloadIndex), IsRegistryLock);
        Assert.DoesNotContain(commands.Commands.Skip(payloadIndex + 1), IsRegistryLock);
        Assert.InRange(savepoints.Created, 0, 1);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var scope = verification.NestedSet<OrderingNode>().ForScope(1);
        Assert.True((await scope.InTree(firstTree)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True((await scope.InTree(secondTree)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Identifies server-side registry lock reads without counting registry revision writes.</summary>
    private static bool IsRegistryLock(string command) =>
        command.Contains("DokaNestedSetTrees_", StringComparison.Ordinal)
        && (command.Contains("UPDLOCK", StringComparison.Ordinal)
            || command.Contains("FOR UPDATE", StringComparison.Ordinal)
            || command.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal));

    /// <summary>Counts savepoints created while the coordinated save owns its transaction.</summary>
    private sealed class SavepointProbe : DbTransactionInterceptor
    {
        internal int Created { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> CreatingSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Created++;

            return ValueTask.FromResult(result);
        }
    }
}
