namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Rollback keeps native membership that the application installed before the save began.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_PayloadOwnerPreexistingNativeBaseline_SurvivesRollback(
        bool publicCurrentValue
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPayloadOwnersAsync(
            database,
            assigned: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var failure = new PlannedParentFailure();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        var graph = await PreparePayloadOwnerRecoveryAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        StagePayloadOwnerWrite(graph, publicCurrentValue);
        var before = CapturePlannedParentState(context);
        var membersBefore = PayloadOwnerNativeMembers(context, graph.Target);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var membersAfter = PayloadOwnerNativeMembers(context, graph.Target);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedOwner = await verification
            .Set<NavigationNode>()
            .Where(node => node.Id == graph.Node.Id)
            .Select(node => node.PayloadOwnerId)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, error);
        AssertPlannedParentState(before, after);
        Assert.Equal(publicCurrentValue, membersBefore.Contains(graph.Node));
        Assert.True(
            membersBefore
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(membersAfter));

        Assert.Contains(graph.Node, graph.Target.Nodes);
        Assert.Equal(2, graph.Node.PayloadOwnerId);
        Assert.True(graph.ForeignKey.IsModified);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(1, storedOwner);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
