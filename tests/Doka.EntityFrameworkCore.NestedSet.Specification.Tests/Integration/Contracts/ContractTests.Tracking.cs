namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ContractTests
{
    /// <summary>Checks persisted identity resolution before tracked-node rejection without hierarchy writes.</summary>
    /// <returns>A task that completes when the rejection has been verified.</returns>
    [Fact]
    public async Task TrackedNodesAreRejectedWithoutClearingTracker()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new CommandProbe();
        var commands = new EnterpriseProbe();
        await using var context = database.CreateContext(probe, commands);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(CancellationToken.None);

        probe.Reset();
        commands.Commands.Clear();

        // Act
        var exception = await Record.ExceptionAsync(() => tree.DeleteSubtreeAsync(1, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(exception).Code);

        // WHY: NodeKey-only mutations must resolve the current persisted tree before rejecting its tracked entries.
        // Other unchanged tracked trees remain valid application state and cannot be rejected globally.
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.UpdateCount);
        var lookup = Assert.Single(commands.Commands);

        Assert.StartsWith("SELECT", lookup.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(TreeNode.Payload), lookup, StringComparison.Ordinal);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
    }

    /// <summary>Verifies that unrelated pending changes survive rejection without executing SQL.</summary>
    /// <returns>A task that completes when the pending-state contract has been verified.</returns>
    [Fact]
    public async Task PendingChangesAreRejectedWithoutClearingTracker()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        var pending = new TextNode
        {
            Id = "pending",
            Tree = "caller",
        };

        await context.AddAsync(pending, CancellationToken.None);
        probe.Reset();

        // Act
        var exception = await Record.ExceptionAsync(() => tree.DeleteSubtreeAsync(1, CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Equal(0, probe.CommandCount);
        Assert.Equal(EntityState.Added, context.Entry(pending).State);
    }

    /// <summary>Verifies that a caller can roll back a mutation without losing transaction ownership.</summary>
    /// <returns>A task that completes when caller ownership and persisted rollback have been verified.</returns>
    [Fact]
    public async Task CallerTransactionRetainsOwnershipAndCanRollBackTreeMutation()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        await using var transaction = await context.Database.BeginTransactionAsync(
            Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            CancellationToken.None);

        // Act
        await tree.DeleteSubtreeAsync(1, CancellationToken.None);
        var retainedOwnership = ReferenceEquals(transaction, context.Database.CurrentTransaction);
        await transaction.RollbackAsync(CancellationToken.None);

        // Assert
        Assert.True(retainedOwnership);
        await using var verification = database.CreateContext();
        Assert.Equal(
            1,
            await verification
                .Set<TreeNode>()
                .Select(x => x.NodeId)
                .SingleAsync(CancellationToken.None));
    }
}
