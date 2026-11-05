namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetMutationFacadeTests
{
    /// <summary>An application transaction can roll back payload and hierarchy work together.</summary>
    [Fact]
    public async Task ApplicationTransactionRollbackIsAtomic()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await ExecuteApplicationTransactionAsync(database, commit: false,
            cancellationToken: TestContext.Current.CancellationToken);

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
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await ExecuteApplicationTransactionAsync(database, commit: true,
            cancellationToken: TestContext.Current.CancellationToken);

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
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task ExecuteApplicationTransactionAsync(
        TestDatabase database,
        bool commit,
        CancellationToken cancellationToken
    )
    {
        await using var context = database.CreateContext();
        var isolation = context.Database.IsSqlite() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted;

        await using var transaction = await context.Database.BeginTransactionAsync(isolation, cancellationToken);
        context.Add(
            new UnrelatedRow
            {
                Id = 1,
                Value = "payload",
            });

        await context.SaveChangesAsync(cancellationToken);
        await context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, cancellationToken);

        if (commit)
        {
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        // WHY: Rollback must release every earlier write even when cancellation ended the caller's operation.
        await transaction.RollbackAsync(CancellationToken.None);
    }
}
