namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks callback writes to an unaffected hierarchy while a managed insertion saves.</summary>
public abstract class ManagedHierarchyPayloadTests : ProviderTest
{
    private static readonly Guid s_existingTree = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_insertedTree = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly RelationalFixture _fixture;
    private readonly OrderingFixture _orderingFixture;

    /// <summary>Uses isolated databases for both unordered and configured-order hierarchy checks.</summary>
    protected ManagedHierarchyPayloadTests(
        IProviderFixture<RelationalFixture> fixture,
        IProviderFixture<OrderingFixture> orderingFixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _orderingFixture = orderingFixture.Value;
    }

    /// <summary>A payload-only callback write in another tree commits with either insertion form.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackPayloadWriteToUnaffectedTreeCommits(
        bool bulk
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedExistingTreeAsync(database);
        var callback = new SaveBoundaryCallback();
        await using var context = database.CreateContext(callback);
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 10, CancellationToken.None);

        callback.OnSaving = _ => tracked.Payload = "updated";
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var inserted = new TreeNode { NodeId = 101 };

        // Act
        if (bulk)
        {
            await hierarchy.InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(s_insertedTree, new NestedSetBranch<TreeNode>(inserted)),],
                CancellationToken.None);
        }
        else
        {
            await hierarchy.InsertRootAsync(inserted, s_insertedTree, CancellationToken.None);
        }

        // Assert
        await using var verification = database.CreateContext();
        Assert.Equal(
            "updated",
            await verification
                .Set<TreeNode>()
                .Where(node => node.NodeId == 10)
                .Select(node => node.Payload)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(2, await verification.Set<TreeNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>A late failure restores an unaffected hierarchy payload to its pending modified state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateFailureRestoresUnaffectedTreePayload(
        bool bulk
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedExistingTreeAsync(database);
        var callback = new SaveBoundaryCallback();
        var failure = new FailAfterSave();
        await using var context = database.CreateContext(callback, failure);
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 10, CancellationToken.None);

        callback.OnSaving = _ => tracked.Payload = "updated";
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var inserted = new TreeNode { NodeId = 101 };

        // Act
        var error = await Record.ExceptionAsync(() => bulk
            ? hierarchy.InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(s_insertedTree, new NestedSetBranch<TreeNode>(inserted)),],
                CancellationToken.None)
            : hierarchy.InsertRootAsync(inserted, s_insertedTree, CancellationToken.None));

        // Assert
        Assert.Same(failure.Error, error);
        Assert.Equal(EntityState.Modified, context.Entry(tracked).State);
        Assert.Equal(
            "stored",
            context
                .Entry(tracked)
                .Property(node => node.Payload)
                .OriginalValue);
        Assert.Equal("updated", tracked.Payload);
        await using var verification = database.CreateContext();
        Assert.Equal(
            "stored",
            await verification
                .Set<TreeNode>()
                .Where(node => node.NodeId == 10)
                .Select(node => node.Payload)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(1, await verification.Set<TreeNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>A callback cannot modify another tree's structural columns during insertion.</summary>
    [Fact]
    public async Task CallbackStructureWriteToUnaffectedTreeIsRejected()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedExistingTreeAsync(database);
        var callback = new SaveBoundaryCallback();
        await using var context = database.CreateContext(callback);
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 10, CancellationToken.None);

        callback.OnSaving = _ => tracked.Start = 99;
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertRootAsync(
            new TreeNode { NodeId = 101 },
            s_insertedTree,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.Set<TreeNode>().CountAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await verification
                .Set<TreeNode>()
                .Where(node => node.NodeId == 10)
                .Select(node => node.Start)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>A configured sort-key change in another tree requires coordinated hierarchy saving.</summary>
    [Fact]
    public async Task CallbackOrderingWriteToUnaffectedTreeIsRejected()
    {
        // Arrange
        await using (var setup = await _orderingFixture.ResetAsync(Engine))
        {
            var existing = setup
                .NestedSet<OrderingNode>()
                .ForScope(7);

            await existing.InsertRootAsync(
                new OrderingNode
                {
                    Id = 10,
                    Name = "stored",
                },
                s_existingTree,
                CancellationToken.None);
        }

        var callback = new SaveBoundaryCallback();
        await using var context = await _orderingFixture.CreateContextAsync(Engine, "Strict", callback);
        var tracked = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 10, CancellationToken.None);

        callback.OnSaving = _ => tracked.Name = "changed";
        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(7);

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertRootAsync(
            new OrderingNode
            {
                Id = 101,
                Name = "target",
            },
            s_insertedTree,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
        await using var verification = await _orderingFixture.CreateContextAsync(Engine);
        Assert.Equal(1, await verification.Set<OrderingNode>().CountAsync(CancellationToken.None));
        Assert.Equal(
            "stored",
            await verification
                .Set<OrderingNode>()
                .Where(node => node.Id == 10)
                .Select(node => node.Name)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Creates one unaffected root before a managed insertion begins.</summary>
    private static async Task SeedExistingTreeAsync(
        TestDatabase database
    )
    {
        await using var setup = database.CreateContext();
        var hierarchy = setup
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(
            new TreeNode
            {
                NodeId = 10,
                Payload = "stored",
            },
            s_existingTree,
            CancellationToken.None);
    }

    /// <summary>Raises one deterministic failure after EF accepted the insertion batch.</summary>
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        internal Exception Error { get; } = new InvalidOperationException("Injected post-save failure.");

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        ) => throw Error;
    }
}
