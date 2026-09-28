namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies immutable import topology and validation before structural database commands.</summary>
public abstract class BulkInputTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Provides an isolated SQLite database for input-boundary assertions.</summary>
    protected BulkInputTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Later mutation of a caller-owned child array cannot alter a branch's copied topology.</summary>
    [Fact]
    public void BranchCopiesItsChildCollection()
    {
        // Arrange
        var child = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 2 });
        var children = new[] { child };
        var branch = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }, children);

        // Act
        children[0] = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 3 });

        // Assert
        Assert.Same(child, Assert.Single(branch.Children));
    }

    /// <summary>An empty forest is a no-op without an unnecessary lock or transaction.</summary>
    [Fact]
    public async Task EmptyForestDoesNotAccessDatabase()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await tree.InsertForestAsync<Guid>([], CancellationToken.None);

        // Assert
        Assert.Empty(probe.Commands);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Assigned duplicate identities are rejected before opening the destination interval.</summary>
    [Fact]
    public async Task DuplicateAssignedKeysAreRejectedBeforeDatabaseWork()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var forest = new[]
        {
            new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }),
            new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }),
        };

        var trees = forest
            .Select((branch, index) => new NestedSetTreeImport<TreeNode, Guid>(
                index == 0 ? Guid.Empty : Guid.NewGuid(),
                branch))
            .ToArray();

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(trees, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(error).Code);
        Assert.Empty(probe.Commands);
    }

    /// <summary>An already-canceled token cannot attach input entities or initialize a lock row.</summary>
    [Fact]
    public async Task CancellationBeforeImportLeavesInputsDetached()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var entity = new TreeNode { NodeId = 1 };

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, new NestedSetBranch<TreeNode>(entity)),],
            new CancellationToken(true)));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Empty(probe.Commands);
        Assert.Equal(EntityState.Detached, context.Entry(entity).State);
    }

    /// <summary>A save callback cannot move inserted entities into another scope before the import completes.</summary>
    [Fact]
    public async Task SavingCallbackCannotAlterManagedScope()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var entity = new TreeNode
        {
            NodeId = 1,
            Tree = 17,
        };

        context.SavingChanges += (_, _) => entity.Tree = 2;

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, new NestedSetBranch<TreeNode>(entity)),],
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(17, entity.Tree);
        Assert.Empty(
            await context
                .Set<TreeNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    ///     A save callback cannot persist a hierarchy node that was absent from the validated branch plan.
    /// </summary>
    [Fact]
    public async Task SavingCallbackCannotAddUnplannedHierarchyNode()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var entity = new TreeNode { NodeId = 1 };
        var unexpected = new TreeNode
        {
            NodeId = 2,
            Tree = 1,
        };

        // WHY: SavingChanges is synchronous; the test must inject a tracked node before that callback returns.
        // ReSharper disable once MethodHasAsyncOverload
        context.SavingChanges += (_, _) => context.Add(unexpected);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, new NestedSetBranch<TreeNode>(entity)),],
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(error).Code);
        Assert.Empty(
            await context
                .Set<TreeNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(EntityState.Detached, context.Entry(entity).State);
        Assert.Same(unexpected, Assert.Single(context.ChangeTracker.Entries()).Entity);
    }

    /// <summary>
    ///     A caller can roll back a successful bulk operation together with its other application writes.
    /// </summary>
    [Fact]
    public async Task SuccessfulImportRemainsInsideCallerTransaction()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            CancellationToken.None);

        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var branch = new NestedSetBranch<TreeNode>(
            new TreeNode { NodeId = 1 },
            [new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 2 })]);

        // Act
        await tree.InsertForestAsync(
            [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None);

        await transaction.RollbackAsync(CancellationToken.None);

        // Assert
        await using var verification = database.CreateContext();
        Assert.Empty(
            await verification
                .Set<TreeNode>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Unrelated tracked hierarchy rows do not masquerade as callback-created import nodes.</summary>
    [Fact]
    public async Task ImportAllowsUnrelatedTrackedHierarchyNode()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var foreign = context
            .NestedSet<TreeNode>()
            .ForScope(2);

        await foreign.InsertRootAsync(new TreeNode { NodeId = 100 }, Guid.NewGuid(), CancellationToken.None);

        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.Tree == 2, CancellationToken.None);

        var target = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var treeId = Guid.NewGuid();

        // Act
        await target.InsertForestAsync(
            [
                new NestedSetTreeImport<TreeNode, Guid>(
                    treeId,
                    new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 200 })),
            ],
            CancellationToken.None);

        var persistedCount = await context
            .Set<TreeNode>()
            .CountAsync(CancellationToken.None);

        var validation = await target
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal(2, persistedCount);
        Assert.True(validation.IsValid);
    }
}
