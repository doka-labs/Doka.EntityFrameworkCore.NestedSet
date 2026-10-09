namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>A scalar-key callback rejection leaves the same input usable for one successful retry.</summary>
    /// <param name="afterSave">Whether the prerequisite mutation occurs after persistence.</param>
    /// <param name="callerTransaction">Whether the caller owns the surrounding transaction.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AssignedScalarKeyFailureAllowsSingleInsertRetry(
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
        var input = NewAssignedSingleScalarInput(Guid.NewGuid());
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var stopMutation = AttachRetryKeyMutation(context, afterSave, () => input.NodeId = 102);
        var rejection = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            input,
            Guid.Empty,
            CancellationToken.None));

        stopMutation();

        // Act
        await tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidStructure,
            Assert.IsType<NestedSetException>(rejection).Code);
        var saved = Assert.Single(
            await context
                .Set<TreeNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(101, saved.NodeId);
        Assert.Equal(
            (101, 1, Guid.Empty, (int?)null, 1L, 2L, 0, 0L),
            (input.NodeId, input.Tree, input.TreeId, input.Parent, input.Start, input.End, input.Depth,
                input.Position));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.NotSame(input, await context.FindAsync<TreeNode>([101], CancellationToken.None));
    }

    /// <summary>An in-place binary-key rejection leaves the same input usable for one successful retry.</summary>
    /// <param name="afterSave">Whether the prerequisite mutation occurs after persistence.</param>
    /// <param name="callerTransaction">Whether the caller owns the surrounding transaction.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AssignedBinaryKeyFailureAllowsSingleInsertRetry(
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
        var input = NewAssignedSingleBinaryInput(Guid.NewGuid());
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);

        var stopMutation = AttachRetryKeyMutation(context, afterSave, () => input.Id[0] = 4);
        var rejection = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            input,
            Guid.Empty,
            CancellationToken.None));

        stopMutation();

        // Act
        await tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidStructure,
            Assert.IsType<NestedSetException>(rejection).Code);

        var saved = Assert.Single(
            await context
                .Set<BinaryNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal<byte>([3], saved.Id);
        Assert.Equal<byte>([3], input.Id);
        Assert.Null(input.ParentId);
        Assert.Equal(
            (1, Guid.Empty, 1L, 2L, 0, 0L),
            (input.Tree, input.TreeId, input.Left, input.Right, input.Depth, input.Position));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.NotSame(input, await context.FindAsync<BinaryNode>([new byte[] { 3 }], CancellationToken.None));
    }

    /// <summary>Allows a callback failure to be disabled before the independently observed retry.</summary>
    private static Action AttachRetryKeyMutation(
        DbContext context,
        bool afterSave,
        Action mutation
    )
    {
        var enabled = true;
        AttachSingleKeyMutation(
            context,
            afterSave,
            () =>
            {
                if (enabled)
                {
                    mutation();
                }
            });

        return () => enabled = false;
    }

    /// <summary>Creates nondefault scalar structure shared by rejection and retry cases.</summary>
    private static TreeNode NewAssignedSingleScalarInput(
        Guid treeId
    ) => new()
    {
        NodeId = 101,
        Tree = 9,
        TreeId = treeId,
        Parent = 8,
        Start = 71,
        End = 72,
        Depth = 3,
        Position = 4,
    };

    /// <summary>Creates nondefault binary structure shared by rejection and retry cases.</summary>
    private static BinaryNode NewAssignedSingleBinaryInput(
        Guid treeId
    ) => new()
    {
        Id = [3],
        Tree = 9,
        TreeId = treeId,
        ParentId = [8],
        Left = 71,
        Right = 72,
        Depth = 3,
        Position = 4,
    };
}
