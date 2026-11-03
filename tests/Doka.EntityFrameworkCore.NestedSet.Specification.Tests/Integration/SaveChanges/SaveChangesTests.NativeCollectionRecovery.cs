namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Unmarked round trips retain native inverse presence independently of pending CLR order.</summary>
    /// <param name="marked">Whether the callback keeps explicit FK write intent after returning to its owner.</param>
    /// <param name="hierarchyFailure">Whether the fault is a real later structural UPDATE or completion.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeCollectionRoundTripRestoresPresenceWithoutConsumingRawOrder(
        bool marked,
        bool hierarchyFailure
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedUnchangedPayloadOwnerAsync(database);
        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback, failure);
        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PreparePayloadOwnerRecoveryAsync(context);
        var originalOrder = graph.Target.Nodes.ToArray();
        var nativeBefore = PayloadOwnerNativeMembers(context, graph.Target);
        var nodeBefore = CapturePlannedParentState(context).Single(entry => ReferenceEquals(entry.Entity, graph.Node));
        var storedBefore = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(database);
        NavigationNode[] pendingOrder = [];
        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            graph.Target.Nodes.Add(graph.Node);
            graph.ForeignKey.CurrentValue = graph.Previous.Id;
            graph.ForeignKey.CurrentValue = graph.Target.Id;
            graph.ForeignKey.IsModified = marked;
            pendingOrder = graph.Target.Nodes.ToArray();
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var nodeAfter = CapturePlannedParentState(context).Single(entry => ReferenceEquals(entry.Entity, graph.Node));
        var nativeAfter = PayloadOwnerNativeMembers(context, graph.Target);
        var storedAfter = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(database);

        // Assert
        Assert.Same(expectedError, error);
        AssertPlannedParentState([nodeBefore], [nodeAfter]);
        Assert.Contains(graph.Node, nativeBefore);
        Assert.True(nativeBefore.ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(nativeAfter));
        Assert.Equal(marked ? originalOrder : pendingOrder, graph.Target.Nodes);
        Assert.Same(graph.Target, graph.Node.PayloadOwner);
        Assert.Equal(graph.Target.Id, graph.Node.PayloadOwnerId);
        Assert.False(graph.ForeignKey.IsModified);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(assignmentsBefore, assignmentsAfter);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
