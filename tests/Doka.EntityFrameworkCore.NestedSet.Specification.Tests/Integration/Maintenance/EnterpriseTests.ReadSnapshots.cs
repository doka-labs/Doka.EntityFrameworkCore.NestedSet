namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>Verifies a deletion between structure and parent reads cannot create a false orphan.</summary>
    /// <returns>A task that completes after checking the original snapshot and concurrent deletion.</returns>
    [EngineFact(
        ExcludedEngines = ["Sqlite", "SqlServer"],
        Reason =
            "The staged concurrent delete requires an MVCC read snapshot; SQLite and the default SQL Server fixture retain blocking read locks.")]
    public async Task ValidationSnapshotSurvivesConcurrentDeleteBetweenReads()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup.AddRangeAsync(
            [
                new TextNode
                {
                    Id = "ROOT",
                    Tree = "scope",
                    Left = 1,
                    Right = 4,
                    Depth = 0,
                    Position = 0,
                },
                new TextNode
                {
                    Id = "child",
                    ParentId = "ROOT",
                    Tree = "scope",
                    Left = 2,
                    Right = 3,
                    Depth = 1,
                    Position = 0,
                },
            ],
            CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var writer = database.CreateContext();
        var mutator = writer
            .NestedSet<TextNode>()
            .ForScope("scope");

        var barrier = new ValidationReadBarrier(token => mutator.DeleteSubtreeAsync("child", token));
        await using var context = database.CreateContext((IInterceptor)barrier);
        var tree = context
            .NestedSet<TextNode>()
            .ForScope("scope")
            .InTree(Guid.Empty);

        // Act
        var issues = (await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues;

        // Assert
        Assert.Empty(issues);
        Assert.Equal(2, barrier.Reads);
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = database.CreateContext();
        var root = Assert.Single(await verification.Set<TextNode>().ToArrayAsync(CancellationToken.None));
        Assert.Equal("ROOT", root.Id);
        Assert.Equal(2, root.Right);
    }

    /// <summary>Verifies snapshot validation preserves caller transactions and their uncommitted payloads.</summary>
    /// <returns>A task that completes after checking transaction ownership and application state.</returns>
    [Fact]
    public async Task ValidationPreservesCallerSerializableTransaction()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(3));
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            CancellationToken.None);

        await context
            .Set<TreeNode>()
            .Where(node => node.NodeId == 2)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Payload, "caller"), CancellationToken.None);

        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        var issues = (await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues;

        // Assert
        Assert.Empty(issues);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal(
            "caller",
            await context
                .Set<TreeNode>()
                .Where(node => node.NodeId == 2)
                .Select(node => node.Payload)
                .SingleAsync(CancellationToken.None));
        await transaction.RollbackAsync(CancellationToken.None);
        await using var verification = database.CreateContext();
        Assert.Equal(
            4096,
            (await verification
                .Set<TreeNode>()
                .Where(node => node.NodeId == 2)
                .Select(node => node.Payload)
                .SingleAsync(CancellationToken.None))!.Length);
    }
}
