namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetMutationFacadeTests
{
    /// <summary>An application transaction can roll back payload and hierarchy work together.</summary>
    [Fact]
    public async Task ApplicationTransactionRollbackIsAtomic()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);

        // Act
        await ExecuteApplicationTransactionAsync(database, commit: false);

        // Assert
        await using var verification = database.CreateContext();
        Assert.Empty(
            await verification
                .Set<UnrelatedRow>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(
            await verification
                .Set<TreeNode>()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>An application transaction can commit payload and hierarchy work together.</summary>
    [Fact]
    public async Task ApplicationTransactionCommitIsAtomic()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);

        // Act
        await ExecuteApplicationTransactionAsync(database, commit: true);

        // Assert
        await using var verification = database.CreateContext();
        Assert.Equal(
            "payload",
            await verification
                .Set<UnrelatedRow>()
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(
            s_firstTree,
            await verification
                .Set<TreeNode>()
                .Select(node => node.TreeId)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Executes the complete caller-owned transaction used by commit and rollback assertions.</summary>
    private static async Task ExecuteApplicationTransactionAsync(
        TestDatabase database,
        bool commit
    )
    {
        await using var context = database.CreateContext();
        var isolation = context.Database.IsSqlite() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted;

        await using var transaction = await context.Database.BeginTransactionAsync(isolation, CancellationToken.None);
        context.Add(
            new UnrelatedRow
            {
                Id = 1,
                Value = "payload",
            });

        await context.SaveChangesAsync(CancellationToken.None);
        await context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);

        if (commit)
        {
            await transaction.CommitAsync(CancellationToken.None);

            return;
        }

        await transaction.RollbackAsync(CancellationToken.None);
    }
}
