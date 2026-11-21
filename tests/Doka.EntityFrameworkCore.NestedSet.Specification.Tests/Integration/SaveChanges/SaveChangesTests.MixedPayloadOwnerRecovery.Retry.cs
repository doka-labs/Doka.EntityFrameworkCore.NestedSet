namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Mixed callback rollback supports independent automatic and explicitly controlled retries.</summary>
    [Theory]
    [InlineData(false, false, "automatic")]
    [InlineData(false, false, "manual-unmarked")]
    [InlineData(false, false, "manual-marked")]
    [InlineData(false, true, "automatic")]
    [InlineData(false, true, "manual-unmarked")]
    [InlineData(false, true, "manual-marked")]
    [InlineData(true, false, "automatic")]
    [InlineData(true, false, "manual-unmarked")]
    [InlineData(true, false, "manual-marked")]
    [InlineData(true, true, "automatic")]
    [InlineData(true, true, "manual-unmarked")]
    [InlineData(true, true, "manual-marked")]
    public async Task SaveChangesAsync_MixedPayloadOwnerRollback_AllowsIndependentRetry(
        bool leavesTarget,
        bool hierarchyFailure,
        string retryPolicy
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

        var bBefore = CapturePlannedParentState(context)
            .Single(entry => ReferenceEquals(entry.Entity, graph.B.Entity));

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StageMixedPayloadOwnerWrites(graph, leavesTarget);
        };

        // WHY: The first failed save is the retry precondition; no native snapshots or CLR membership are repaired.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var bAfterFailure = CapturePlannedParentState(context)
            .Single(entry => ReferenceEquals(entry.Entity, graph.B.Entity));

        var failedNative = PayloadOwnerNativeMembers(context, graph.A.Target);
        var failedPendingA = graph.A.Target.Nodes.Contains(graph.A.Node);
        var failedForeignKey = graph.A.Node.PayloadOwnerId;
        var failedModified = graph.A.ForeignKey.IsModified;
        context.ChangeTracker.AutoDetectChangesEnabled = retryPolicy == "automatic";

        if (retryPolicy == "manual-marked")
        {
            // WHY: A manual retry explicitly reissues A's FK write; an unmarked retry must keep it unsaved.
            StagePayloadOwnerWrite(graph.A, publicCurrentValue: false);
        }

        // Act
        var retryError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var stored = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignments = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var native = PayloadOwnerNativeMembers(context, graph.A.Target)
            .Cast<NavigationNode>()
            .Select(node => node.Id)
            .Order()
            .ToArray();

        var current = graph
            .A
            .Target
            .Nodes
            .Select(node => node.Id)
            .Order()
            .ToArray();

        var persisted = assignments
            .Where(assignment => assignment.PayloadOwnerId == graph.A.Target.Id)
            .Select(assignment => assignment.Id)
            .Order()
            .ToArray();

        // Assert
        Assert.Same(expectedError, firstError);
        AssertPlannedParentState([bBefore], [bAfterFailure]);
        Assert.DoesNotContain(graph.A.Node, failedNative);
        Assert.Equal(leavesTarget, failedNative.Contains(graph.B.Entity));
        Assert.True(failedPendingA);
        Assert.Equal(1, failedForeignKey);
        Assert.False(failedModified);
        Assert.Null(retryError);
        var savesA = retryPolicy != "manual-unmarked";
        Assert.Equal(
            savesA
                ? 2
                : 1,
            assignments.Single(assignment => assignment.Id == graph.A.Node.Id)
                .PayloadOwnerId);

        Assert.Equal(
            leavesTarget
                ? 2
                : 1,
            assignments.Single(assignment => assignment.Id == graph.B.Entity.Id)
                .PayloadOwnerId);

        Assert.Equal(persisted, native);

        if (savesA)
        {
            Assert.Equal(persisted, current);
        }
        else
        {
            Assert.Contains(graph.A.Node.Id, current);
            Assert.DoesNotContain(graph.A.Node.Id, persisted);
        }

        Assert.Equal(leavesTarget, graph.A.Target.Nodes.Contains(graph.B.Entity));
        Assert.Same(
            leavesTarget
                ? graph.A.Target
                : graph.A.Previous,
            graph.B.Entity.PayloadOwner);

        Assert.False(graph.BForeignKey.IsModified);
        Assert.Equal(
            "Zulu",
            stored.Nodes.Single(node => node.Id == graph.A.Node.Id)
                .Name);

        Assert.Equal(0, stored.Issues);
        Assert.Equal(retryPolicy == "automatic", context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Detached pending members remain guarded while restored known native members remain usable.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SaveChangesAsync_MixedPayloadOwnerRollback_PreservesDetachedFollowupGuards(
        bool leavesTarget,
        bool hierarchyFailure,
        bool detachRawMember
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
        var probe = new NavigationWriteProbe();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure,
            probe);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareMixedPayloadOwnerGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

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

        // WHY: This rollback leaves raw A pending but returns B to its independently installed native relationship.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var nativeAfterFailure = PayloadOwnerNativeMembers(context, graph.A.Target);

        if (detachRawMember)
        {
            graph.A.Entry.State = EntityState.Detached;
        }
        else
        {
            graph.B.State = EntityState.Detached;

            // WHY: Cancel only the unrelated order edit so this follow-up tests ordinary payload persistence.
            graph.A.Entry.Property(node => node.Name)
                .IsModified = false;
        }

        graph.A.Note.CurrentValue = "following payload";
        var writesBeforeFollowup = probe.Writes;

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expectedError, firstError);
        Assert.DoesNotContain(graph.A.Node, nativeAfterFailure);
        Assert.Equal(leavesTarget, nativeAfterFailure.Contains(graph.B.Entity));
        Assert.Contains(graph.A.Node, graph.A.Target.Nodes);
        Assert.Equal(leavesTarget, graph.A.Target.Nodes.Contains(graph.B.Entity));

        if (detachRawMember)
        {
            Assert.Equal(
                NestedSetErrorCode.InvalidContext,
                Assert.IsType<NestedSetException>(error)
                    .Code);

            Assert.Equal(writesBeforeFollowup, probe.Writes);
            Assert.Equal(storedBefore.Note, storedAfter.Note);
        }
        else
        {
            Assert.Null(error);
            Assert.Equal("following payload", storedAfter.Note);
        }

        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(assignmentsBefore, assignmentsAfter);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
