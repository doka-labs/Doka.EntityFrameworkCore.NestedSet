namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that managed single insertions reject callback edits to their staged topology.</summary>
public abstract partial class SingleInsertGuardTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Shares the existing provider lifecycle and mapping for all callback guard cases.</summary>
    protected SingleInsertGuardTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Rolls back a callback's structural edit without discarding earlier caller work.</summary>
    [Theory]
    [InlineData(nameof(TreeNode.Depth))]
    [InlineData(nameof(TreeNode.Tree))]
    [InlineData(nameof(TreeNode.Parent))]
    public async Task CallbackCannotChangeSingleInsertionStructure(
        string property
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            CancellationToken.None);

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var root = new TreeNode
        {
            NodeId = 101,
            Tree = 9,
            Start = 71,
            End = 72,
            Depth = 3,
            Position = 4,
        };
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        context.SavingChanges += (_, _) => context
            .Entry(root)
            .Property(property)
            .CurrentValue = 99;

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal((9, 71, 72, 3, 4), (root.Tree, root.Start, root.End, root.Depth, root.Position));
        Assert.Null(root.Parent);
        Assert.Equal(EntityState.Detached, context.Entry(root).State);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(1, await context.Set<UnrelatedRow>().CountAsync(CancellationToken.None));
        Assert.Empty(
            await context
                .Set<TreeNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>Rejects an extra hierarchy entry introduced before or after the managed INSERT wave.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackCannotAddUnplannedSingleInsertionNode(
        bool afterSave
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var root = new TreeNode
        {
            NodeId = 101,
            Tree = 9,
            Start = 71,
            End = 72,
        };

        var unexpected = new TreeNode
        {
            NodeId = 102,
            Tree = 2,
            Start = 1,
            End = 2,
        };

        if (afterSave)
        {
            // WHY: EF invokes this event synchronously; the entry must be added before the callback returns.
            // ReSharper disable once MethodHasAsyncOverload
            context.SavedChanges += (_, _) => context.Add(unexpected);
        }
        else
        {
            // WHY: EF invokes this event synchronously; the entry must be added before the callback returns.
            // ReSharper disable once MethodHasAsyncOverload
            context.SavingChanges += (_, _) => context.Add(unexpected);
        }

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal((9, 71, 72), (root.Tree, root.Start, root.End));
        Assert.Equal(EntityState.Detached, context.Entry(root).State);
        Assert.Same(unexpected, Assert.Single(context.ChangeTracker.Entries()).Entity);

        // WHY: The transaction rolled back this callback-created entity. Added truthfully preserves the caller's
        // unsaved work; Unchanged would claim that a row now absent from the database had been persisted.
        Assert.Equal(EntityState.Added, context.Entry(unexpected).State);
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Set<TreeNode>().ToArrayAsync(CancellationToken.None));
    }

    /// <summary>A later save interceptor can add an application row to the same managed insert.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateInterceptorCanAddUnrelatedManagedInsertWrite(
        bool bulk
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var callback = new SaveBoundaryCallback();
        await using var context = database.CreateContext(callback);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var root = new TreeNode { NodeId = 101 };
        var audit = new UnrelatedRow { Id = 701 };

        // WHY: This synchronous SavingChanges callback must finish attaching the audit before it returns.
        // The configured identity generator does not query the database during Add.
        // ReSharper disable once MethodHasAsyncOverload
        callback.OnSaving = current => current.Add(audit);

        // Act
        if (bulk)
        {
            await tree.InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, new NestedSetBranch<TreeNode>(root)),],
                CancellationToken.None);
        }
        else
        {
            await tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
        }

        // Assert
        await using var verification = database.CreateContext();
        Assert.Equal(101, Assert.Single(await verification.Set<TreeNode>().ToArrayAsync(CancellationToken.None)).NodeId);
        Assert.Equal(701, Assert.Single(await verification.Set<UnrelatedRow>().ToArrayAsync(CancellationToken.None)).Id);
    }

    /// <summary>A failed application write rolls back the hierarchy row from the same managed save.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCallbackAuditWriteRollsBackManagedInsert(
        bool bulk
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using (var setup = database.CreateContext())
        {
            await setup.AddAsync(new UnrelatedRow { Id = 701 }, CancellationToken.None);
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        var callback = new SaveBoundaryCallback();
        await using var context = database.CreateContext(callback);

        // WHY: This synchronous SavingChanges callback must attach the assigned-key row before it returns.
        // Its key is already supplied, so Add does not require asynchronous database access.
        // ReSharper disable once MethodHasAsyncOverload
        callback.OnSaving = current => current.Add(new UnrelatedRow { Id = 701 });
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var root = new TreeNode { NodeId = 101 };

        // Act
        var error = await Record.ExceptionAsync(() => bulk
            ? hierarchy.InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, new NestedSetBranch<TreeNode>(root)),],
                CancellationToken.None)
            : hierarchy.InsertRootAsync(root, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.IsType<DbUpdateException>(error);
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Set<TreeNode>().ToArrayAsync(CancellationToken.None));
        Assert.Equal(701, Assert.Single(await verification.Set<UnrelatedRow>().ToArrayAsync(CancellationToken.None)).Id);
    }
}
