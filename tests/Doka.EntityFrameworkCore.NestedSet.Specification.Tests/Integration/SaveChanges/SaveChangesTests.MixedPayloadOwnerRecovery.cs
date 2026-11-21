namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Raw and native callback FK writes restore each member's independent pre-save relationship.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveChangesAsync_MixedPayloadOwnerRollback_RestoresIndependentMembers(
        bool leavesTarget,
        bool hierarchyFailure
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedMixedPayloadOwnerMemberAsync(
            database,
            leavesTarget,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareMixedPayloadOwnerGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var before = CapturePlannedParentState(context);
        var previousCollection = graph.A.Previous.Nodes;
        var previousMembers = previousCollection.ToArray();
        var targetCollection = graph.A.Target.Nodes;
        var targetMembers = targetCollection.ToArray();
        var previousNative = PayloadOwnerNativeMembers(context, graph.A.Previous);
        var targetNative = PayloadOwnerNativeMembers(context, graph.A.Target);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StageMixedPayloadOwnerWrites(graph, leavesTarget);
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var previousNativeAfter = PayloadOwnerNativeMembers(context, graph.A.Previous);
        var targetNativeAfter = PayloadOwnerNativeMembers(context, graph.A.Target);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expectedError, error);
        AssertPlannedParentState(
            before
                .Where(entry => ReferenceEquals(entry.Entity, graph.A.Node)
                    || ReferenceEquals(entry.Entity, graph.B.Entity))
                .ToArray(),
            after
                .Where(entry => ReferenceEquals(entry.Entity, graph.A.Node)
                    || ReferenceEquals(entry.Entity, graph.B.Entity))
                .ToArray());

        Assert.Same(previousCollection, graph.A.Previous.Nodes);
        Assert.Same(targetCollection, graph.A.Target.Nodes);
        Assert.Equal(previousMembers, graph.A.Previous.Nodes, ReferenceEqualityComparer.Instance);

        // WHY: A's raw membership remains pending, while B must return to its original CLR owner independently.
        Assert.Equal(
            targetMembers,
            graph.A.Target.Nodes.Where(node => !ReferenceEquals(node, graph.A.Node)),
            ReferenceEqualityComparer.Instance);

        Assert.Contains(graph.A.Node, graph.A.Target.Nodes);
        Assert.True(
            previousNative
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(previousNativeAfter));

        Assert.True(
            targetNative
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(targetNativeAfter));

        Assert.DoesNotContain(graph.A.Node, targetNativeAfter);
        Assert.Equal(leavesTarget, graph.A.Target.Nodes.Contains(graph.B.Entity));
        Assert.Equal(!leavesTarget, graph.A.Previous.Nodes.Contains(graph.B.Entity));
        Assert.Equal(
            leavesTarget
                ? 2
                : 1,
            graph.B.Entity.PayloadOwnerId);

        Assert.Same(
            leavesTarget
                ? graph.A.Target
                : graph.A.Previous,
            graph.B.Entity.PayloadOwner);

        Assert.False(graph.BForeignKey.IsModified);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(assignmentsBefore, assignmentsAfter);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>An authoritative pre-save member survives undoing a later library insertion of that member.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_MixedPayloadOwnerRollback_RetainsAuthoritativePreSaveMembership(
        bool hierarchyFailure
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedMixedPayloadOwnerMemberAsync(
            database,
            leavesTarget: false,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareMixedPayloadOwnerGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        StagePayloadOwnerWrite(graph.A, publicCurrentValue: true);
        var before = CapturePlannedParentState(context);
        var nativeBefore = PayloadOwnerNativeMembers(context, graph.A.Target);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;

            // WHY: Genuine EF fixup removes A, then the raw write forces the library to baseline A again.
            graph.A.ForeignKey.CurrentValue = graph.A.Previous.Id;
            StagePayloadOwnerWrite(graph.A, publicCurrentValue: false);
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var nativeAfter = PayloadOwnerNativeMembers(context, graph.A.Target);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertPlannedParentState(before, after);
        Assert.Same(expectedError, error);
        Assert.Contains(graph.A.Node, nativeBefore);
        Assert.True(
            nativeBefore
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(nativeAfter));

        Assert.Contains(graph.A.Node, nativeAfter);
        Assert.Contains(graph.A.Node, graph.A.Target.Nodes);
        Assert.DoesNotContain(graph.A.Node, graph.A.Previous.Nodes);
        Assert.Same(graph.A.Target, graph.A.Node.PayloadOwner);
        Assert.Equal(2, graph.A.Node.PayloadOwnerId);
        Assert.True(graph.A.ForeignKey.IsModified);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(assignmentsBefore, assignmentsAfter);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Changes B through real fixup before A's prepared raw membership is installed by the library.</summary>
    private static void StageMixedPayloadOwnerWrites(
        MixedPayloadOwnerGraph graph,
        bool leavesTarget
    )
    {
        graph.BForeignKey.CurrentValue = leavesTarget
            ? graph.A.Previous.Id
            : graph.A.Target.Id;

        StagePayloadOwnerWrite(graph.A, publicCurrentValue: false);
    }

    /// <summary>Reads participating stored FK values independently of the restored tracker or CLR graph.</summary>
    private static async Task<MixedPayloadOwnerAssignment[]> ReadMixedPayloadOwnerAssignmentsAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: cancellationToken);

        return await verification
            .Set<NavigationNode>()
            .Where(node => node.Id >= 2 && node.Id <= 4)
            .OrderBy(node => node.Id)
            .Select(node => new MixedPayloadOwnerAssignment(node.Id, node.PayloadOwnerId))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Holds exact tracked objects and property entries for two independent application writes.</summary>
    private sealed record MixedPayloadOwnerGraph(
        PayloadOwnerRecoveryGraph A,
        EntityEntry<NavigationNode> B,
        PropertyEntry<NavigationNode, int?> BForeignKey
    );

    /// <summary>Captures persisted relationship identity without materializing tracked entities.</summary>
    private sealed record MixedPayloadOwnerAssignment(
        int Id,
        int? PayloadOwnerId
    );
}
