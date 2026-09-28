namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>Verifies validation inspects stored structure without requiring or accepting a clean tracker.</summary>
    /// <param name="caller">Whether an application-owned transaction already exists.</param>
    /// <param name="pending">Whether a tracked node has an unsaved, deliberately invalid depth.</param>
    /// <returns>A task that completes after checking stored values, tracked state, and transaction ownership.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ValidationKeepsTrackedValuesAndCallerOwnership(
        bool caller,
        bool pending
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(3));
        await using var context = database.CreateContext();
        await using var transaction = caller ? await BeginCallerAsync(context) : null;
        var tracked = await context
            .Set<TreeNode>()
            .SingleAsync(node => node.NodeId == 2, CancellationToken.None);

        tracked.Depth = pending ? 100 : 1;
        context.ChangeTracker.DetectChanges();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        var issues = (await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues;

        // Assert
        Assert.Empty(issues);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal(pending ? 100 : 1, tracked.Depth);
        Assert.Equal(pending ? EntityState.Modified : EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal(
            1,
            await context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.NodeId == 2)
                .Select(node => node.Depth)
                .SingleAsync(CancellationToken.None));
    }
}
