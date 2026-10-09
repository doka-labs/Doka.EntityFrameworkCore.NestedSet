namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>Rejects assigned scalar-key callbacks and restores the exact detached input.</summary>
    /// <param name="afterSave">Whether the mutation occurs after persistence.</param>
    /// <param name="callerTransaction">Whether the caller owns the surrounding transaction.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AssignedScalarKeyMutationRollsBackSingleInsert(
        bool afterSave,
        bool callerTransaction
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await using var transaction = callerTransaction
            ? await context.Database.BeginTransactionAsync(
                Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                CancellationToken.None)
            : null;

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var originalTreeId = Guid.NewGuid();
        var input = NewAssignedSingleScalarInput(originalTreeId);

        var tree = context.NestedSet<TreeNode>().ForScope(1);
        AttachSingleKeyMutation(context, afterSave, () => input.NodeId = 102);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None));
        var restored = (input.NodeId, input.Tree, input.TreeId, input.Parent, input.Start, input.End,
            input.Depth, input.Position);

        var tracked = context.ChangeTracker.Entries().Select(entry => entry.Entity).ToArray();
        var originalLookup = await context.FindAsync<TreeNode>([101], CancellationToken.None);
        var changedLookup = await context.FindAsync<TreeNode>([102], CancellationToken.None);
        var rolledBackRows = await context.Set<TreeNode>().AsNoTracking().CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal((101, 9, originalTreeId, (int?)8, 71L, 72L, 3, 4L), restored);
        Assert.Same(marker, Assert.Single(tracked));
        Assert.Null(originalLookup);
        Assert.Null(changedLookup);
        Assert.Equal(0, rolledBackRows);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(1, await context.Set<UnrelatedRow>().CountAsync(CancellationToken.None));
        Assert.Same(transaction, context.Database.CurrentTransaction);
    }

    /// <summary>Rejects in-place assigned binary-key callbacks without retaining a stale native identity.</summary>
    /// <param name="afterSave">Whether the mutation occurs after persistence.</param>
    /// <param name="callerTransaction">Whether the caller owns the surrounding transaction.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AssignedBinaryKeyMutationRollsBackSingleInsert(
        bool afterSave,
        bool callerTransaction
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await using var transaction = callerTransaction
            ? await context.Database.BeginTransactionAsync(
                Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                CancellationToken.None)
            : null;

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var originalTreeId = Guid.NewGuid();
        var input = NewAssignedSingleBinaryInput(originalTreeId);

        var tree = context.NestedSet<BinaryNode>().ForScope(1);

        // WHY: Mutating the installed buffer also changes its hash unless native identity storage is isolated.
        AttachSingleKeyMutation(context, afterSave, () => input.Id[0] = 4);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None));
        var restoredKey = input.Id.ToArray();
        var restoredParent = input.ParentId?.ToArray();
        var restored = (input.Tree, input.TreeId, input.Left, input.Right, input.Depth, input.Position);
        var tracked = context.ChangeTracker.Entries().Select(entry => entry.Entity).ToArray();
        var originalLookup = await context.FindAsync<BinaryNode>([new byte[] { 3 }], CancellationToken.None);
        var changedLookup = await context.FindAsync<BinaryNode>([new byte[] { 4 }], CancellationToken.None);
        var rolledBackRows = await context.Set<BinaryNode>().AsNoTracking().CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal<byte>([3], restoredKey);
        Assert.Equal<byte>([8], restoredParent!);
        Assert.Equal((9, originalTreeId, 71L, 72L, 3, 4L), restored);
        Assert.Same(marker, Assert.Single(tracked));
        Assert.Null(originalLookup);
        Assert.Null(changedLookup);
        Assert.Equal(0, rolledBackRows);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(1, await context.Set<UnrelatedRow>().CountAsync(CancellationToken.None));
        Assert.Same(transaction, context.Database.CurrentTransaction);
    }

    /// <summary>Runs the exact same mutation on either side of the managed database save.</summary>
    private static void AttachSingleKeyMutation(
        DbContext context,
        bool afterSave,
        Action mutate
    )
    {
        if (afterSave)
        {
            context.SavedChanges += (_, _) => mutate();
        }
        else
        {
            context.SavingChanges += (_, _) => mutate();
        }
    }
}
