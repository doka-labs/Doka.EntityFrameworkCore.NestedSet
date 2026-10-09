namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies atomic bulk insertion with custom mapped properties and real relational transactions.</summary>
public abstract class BulkInsertTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses an isolated fixture-owned database for each supported engine.</summary>
    protected BulkInsertTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    ///     One reserved gap imports an assigned-key subtree without finalization or payload materialization.
    /// </summary>
    [Fact]
    public async Task WideSubtreeUsesOneGapAndBatchedWrites()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);

        await setup
            .NestedSet<TreeNode>()
            .ForScope(2)
            .InsertRootAsync(new TreeNode { NodeId = 999 }, Guid.Empty, CancellationToken.None);

        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var children = Enumerable
            .Range(3, 130)
            .Select(id => new NestedSetBranch<TreeNode>(new TreeNode { NodeId = id }))
            .ToArray();

        var root = new TreeNode { NodeId = 2 };
        var branch = new NestedSetBranch<TreeNode>(root, children);

        // Act
        await tree.InsertSubtreeAsync(branch, 1, CancellationToken.None);

        // Assert
        Assert.Equal(1, probe.NodeUpdates);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.InRange(probe.MaximumUpdateParameters, 1, 999);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal((2, 263, 1, 0, 1), (root.Start, root.End, root.Depth, root.Position, root.Parent));
        Assert.All(children, child => Assert.Equal(2, child.Entity.Parent));
        Assert.Equal(
            Enumerable.Range(1, 132),
            await tree
                .TreeContaining(2)
                .Select(node => node.NodeId)
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            2,
            await setup
                .Set<TreeNode>()
                .Where(node => node.NodeId == 999)
                .Select(node => node.End)
                .SingleAsync(CancellationToken.None));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Input topology controls multiple independent roots and supports arbitrarily deep branches.</summary>
    [Fact]
    public async Task DeepForestDoesNotRequireRecursiveCalls()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var nodes = Enumerable
            .Range(1, 512)
            .Select(id => new TreeNode { NodeId = id })
            .ToArray();

        var branch = new NestedSetBranch<TreeNode>(nodes[^1]);

        for (var index = nodes.Length - 2; index >= 0; index--)
        {
            branch = new NestedSetBranch<TreeNode>(nodes[index], [branch]);
        }

        var otherRoot = new TreeNode { NodeId = 1000 };

        // Act
        await tree.InsertForestAsync(
            [
                new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, branch),
                new NestedSetTreeImport<TreeNode, Guid>(Guid.NewGuid(), new NestedSetBranch<TreeNode>(otherRoot)),
            ],
            CancellationToken.None);

        // Assert
        Assert.Equal(511, nodes[^1].Depth);
        Assert.Equal(1024, nodes[0].End);
        Assert.Equal((1, 2, 0, 0), (otherRoot.Start, otherRoot.End, otherRoot.Depth, otherRoot.Position));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
        Assert.Empty(
            (await tree
                .InTree(otherRoot.TreeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
        Assert.NotEqual(nodes[0].TreeId, otherRoot.TreeId);
    }

    /// <summary>Repeated references cannot silently create a cycle or give one entity multiple parents.</summary>
    /// <param name="differentTrees">Whether the same entity occurs across tree plans instead of within one plan.</param>
    /// <returns>A task that completes after checking pre-write rejection and unchanged inputs.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedEntityIsRejectedBeforeDatabaseWork(
        bool differentTrees
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext(probe);
        var entity = new TreeNode
        {
            NodeId = 1,
            Start = 17,
            End = 18,
            Tree = 9,
        };

        NestedSetTreeImport<TreeNode, Guid>[] trees = differentTrees
            ? [new(Guid.Empty, new(entity)), new(Guid.NewGuid(), new(entity))]
            : [new(Guid.Empty, new(entity, [new(entity)]))];

        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            trees,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(error).Code);
        Assert.Empty(probe.Commands);
        Assert.Equal((17, 18, 9), (entity.Start, entity.End, entity.Tree));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>A parent from another scope cannot become an import destination.</summary>
    [Fact]
    public async Task MissingScopedParentDoesNotOpenGap()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup
            .NestedSet<TreeNode>()
            .ForScope(2)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);

        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var entity = new TreeNode { NodeId = 2 };

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertSubtreeAsync(
            new NestedSetBranch<TreeNode>(entity),
            1,
            CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Equal(0, probe.NodeUpdates);
        Assert.Single(
            await setup
                .Set<TreeNode>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(0, entity.Start);
    }

    /// <summary>
    ///     Cancellation after the gap and insert wave restores both database and every imported CLR value.
    /// </summary>
    [Fact]
    public async Task MidImportCancellationRollsBackGapAndRestoresInputs()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);

        using var cancellation = new CancellationTokenSource();
        var probe = new BulkRefreshFailure(nameof(TreeNode), cancellation);
        await using var context = database.CreateContext(probe);
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var root = new TreeNode
        {
            NodeId = 2,
            Start = 71,
            End = 72,
            Tree = 7,
            Parent = 700,
        };

        var child = new TreeNode
        {
            NodeId = 3,
            Start = 81,
            End = 82,
            Tree = 8,
        };

        var branch = new NestedSetBranch<TreeNode>(root, [new NestedSetBranch<TreeNode>(child)]);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertSubtreeAsync(branch, 1, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.True(probe.ReachedRefresh);
        Assert.Equal((71, 72, 7, 700), (root.Start, root.End, root.Tree, root.Parent));
        Assert.Equal((81, 82, 8), (child.Start, child.End, child.Tree));
        Assert.Empty(context.ChangeTracker.Entries());
        var persisted = Assert.Single(
            await setup
                .Set<TreeNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal((1, 2), (persisted.Start, persisted.End));
    }

    /// <summary>
    ///     An import savepoint can fail without discarding earlier application work or its caller transaction.
    /// </summary>
    [Fact]
    public async Task CallerTransactionSurvivesFailedImport()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new BulkRefreshFailure(nameof(TreeNode));
        await using var context = database.CreateContext(probe);
        await using var transaction = await context.Database.BeginTransactionAsync(
            Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            CancellationToken.None);

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var root = new TreeNode { NodeId = 1 };
        var branch = new NestedSetBranch<TreeNode>(root, [new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 2 })]);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.Empty(
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(0, root.Start);
    }
}
