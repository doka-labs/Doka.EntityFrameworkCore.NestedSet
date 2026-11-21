namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Each newly installed raw native member is undone without removing pending CLR occurrences.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_MixedPayloadOwnerRollback_PreservesRawMembersAndDuplicateClrOccurrences(
        bool duplicate
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedMixedPayloadOwnerMemberAsync(
            database,
            leavesTarget: false,
            cancellationToken: TestContext.Current.CancellationToken);

        await using (var setup = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            await setup
                .NestedSet<NavigationNode>()
                .ForScope(1)
                .InsertChildAsync(
                    new NavigationNode
                    {
                        Id = 4,
                        Name = "Charlie",
                        PayloadOwnerId = 1,
                    },
                    1,
                    CancellationToken.None);
        }

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        var graph = await PrepareMixedPayloadOwnerGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var c = await context
            .Set<NavigationNode>()
            .SingleAsync(node => node.Id == 4, CancellationToken.None);

        var cEntry = context.Entry(c);
        var cForeignKey = cEntry.Property(node => node.PayloadOwnerId);
        var before = CapturePlannedParentState(context);
        var nativeBefore = PayloadOwnerNativeMembers(context, graph.A.Target);
        var previousNative = PayloadOwnerNativeMembers(context, graph.A.Previous);
        var collection = graph.A.Target.Nodes;
        NavigationNode[] pending = duplicate
            ? [graph.A.Node, c, graph.A.Node]
            : [graph.A.Node, c];

        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StageMixedPayloadOwnerWrites(graph, leavesTarget: false);
            c.PayloadOwnerId = graph.A.Target.Id;
            cForeignKey.IsModified = true;
            collection.Add(c);

            if (duplicate)
            {
                collection.Add(graph.A.Node);
            }
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var nativeAfter = PayloadOwnerNativeMembers(context, graph.A.Target);
        var previousNativeAfter = PayloadOwnerNativeMembers(context, graph.A.Previous);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, error);
        AssertPlannedParentState(
            before
                .Where(entry => entry.Entity is NavigationNode)
                .ToArray(),
            after
                .Where(entry => entry.Entity is NavigationNode)
                .ToArray());

        Assert.Same(collection, graph.A.Target.Nodes);
        Assert.Equal(pending, collection, ReferenceEqualityComparer.Instance);
        Assert.True(
            nativeBefore
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(nativeAfter));

        Assert.True(
            previousNative
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(previousNativeAfter));

        Assert.DoesNotContain(graph.A.Node, nativeAfter);
        Assert.DoesNotContain(c, nativeAfter);
        Assert.DoesNotContain(graph.B.Entity, collection);
        Assert.Same(graph.A.Previous, graph.B.Entity.PayloadOwner);
        Assert.Equal(1, c.PayloadOwnerId);
        Assert.False(cForeignKey.IsModified);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(assignmentsBefore, assignmentsAfter);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
