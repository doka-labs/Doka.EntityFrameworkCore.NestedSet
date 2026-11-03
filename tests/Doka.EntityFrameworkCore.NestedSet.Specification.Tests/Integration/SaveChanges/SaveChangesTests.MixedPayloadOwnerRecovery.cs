namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Raw and native callback FK writes restore each member's independent pre-save relationship.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MixedPayloadOwnerRollbackRestoresIndependentMembers(bool leavesTarget, bool hierarchyFailure)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedMixedPayloadOwnerMemberAsync(database, leavesTarget);
        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback, failure);
        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareMixedPayloadOwnerGraphAsync(context);
        var before = CapturePlannedParentState(context);
        var previousCollection = graph.A.Previous.Nodes;
        var previousMembers = previousCollection.ToArray();
        var targetCollection = graph.A.Target.Nodes;
        var targetMembers = targetCollection.ToArray();
        var previousNative = PayloadOwnerNativeMembers(context, graph.A.Previous);
        var targetNative = PayloadOwnerNativeMembers(context, graph.A.Target);
        var storedBefore = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(database);
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
        var storedAfter = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(database);

        // Assert
        Assert.Same(expectedError, error);
        AssertPlannedParentState(
            before.Where(entry => ReferenceEquals(entry.Entity, graph.A.Node)
                || ReferenceEquals(entry.Entity, graph.B.Entity)).ToArray(),
            after.Where(entry => ReferenceEquals(entry.Entity, graph.A.Node)
                || ReferenceEquals(entry.Entity, graph.B.Entity)).ToArray());

        Assert.Same(previousCollection, graph.A.Previous.Nodes);
        Assert.Same(targetCollection, graph.A.Target.Nodes);
        Assert.Equal(previousMembers, graph.A.Previous.Nodes, ReferenceEqualityComparer.Instance);
        // WHY: A's raw membership remains pending, while B must return to its original CLR owner independently.
        Assert.Equal(targetMembers, graph.A.Target.Nodes.Where(node => !ReferenceEquals(node, graph.A.Node)),
            ReferenceEqualityComparer.Instance);

        Assert.Contains(graph.A.Node, graph.A.Target.Nodes);
        Assert.True(previousNative.ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(previousNativeAfter));
        Assert.True(targetNative.ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(targetNativeAfter));
        Assert.DoesNotContain(graph.A.Node, targetNativeAfter);
        Assert.Equal(leavesTarget, graph.A.Target.Nodes.Contains(graph.B.Entity));
        Assert.Equal(!leavesTarget, graph.A.Previous.Nodes.Contains(graph.B.Entity));
        Assert.Equal(leavesTarget ? 2 : 1, graph.B.Entity.PayloadOwnerId);
        Assert.Same(leavesTarget ? graph.A.Target : graph.A.Previous, graph.B.Entity.PayloadOwner);
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
    public async Task MixedPayloadOwnerRollbackRetainsAuthoritativePreSaveMembership(bool hierarchyFailure)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedMixedPayloadOwnerMemberAsync(database, leavesTarget: false);
        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback, failure);
        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareMixedPayloadOwnerGraphAsync(context);
        StagePayloadOwnerWrite(graph.A, publicCurrentValue: true);
        var before = CapturePlannedParentState(context);
        var nativeBefore = PayloadOwnerNativeMembers(context, graph.A.Target);
        var storedBefore = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(database);
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
        var storedAfter = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(database);

        // Assert
        AssertPlannedParentState(before, after);
        Assert.Same(expectedError, error);
        Assert.Contains(graph.A.Node, nativeBefore);
        Assert.True(nativeBefore.ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(nativeAfter));
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
    public async Task MixedPayloadOwnerRollbackAllowsIndependentRetry(
        bool leavesTarget,
        bool hierarchyFailure,
        string retryPolicy
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedMixedPayloadOwnerMemberAsync(database, leavesTarget);
        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback, failure);
        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareMixedPayloadOwnerGraphAsync(context);
        var bBefore = CapturePlannedParentState(context).Single(entry => ReferenceEquals(entry.Entity, graph.B.Entity));
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
        var stored = await ReadPreparedOwnerStoreAsync(database);
        var assignments = await ReadMixedPayloadOwnerAssignmentsAsync(database);
        var native = PayloadOwnerNativeMembers(context, graph.A.Target).Cast<NavigationNode>()
            .Select(node => node.Id).Order().ToArray();

        var current = graph.A.Target.Nodes.Select(node => node.Id).Order().ToArray();
        var persisted = assignments.Where(assignment => assignment.PayloadOwnerId == graph.A.Target.Id)
            .Select(assignment => assignment.Id).Order().ToArray();

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
        Assert.Equal(savesA ? 2 : 1, assignments.Single(assignment => assignment.Id == graph.A.Node.Id).PayloadOwnerId);
        Assert.Equal(leavesTarget ? 2 : 1,
            assignments.Single(assignment => assignment.Id == graph.B.Entity.Id).PayloadOwnerId);

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
        Assert.Same(leavesTarget ? graph.A.Target : graph.A.Previous, graph.B.Entity.PayloadOwner);
        Assert.False(graph.BForeignKey.IsModified);
        Assert.Equal("Zulu", stored.Nodes.Single(node => node.Id == graph.A.Node.Id).Name);
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
    public async Task MixedPayloadOwnerRollbackPreservesDetachedFollowupGuards(
        bool leavesTarget,
        bool hierarchyFailure,
        bool detachRawMember
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedMixedPayloadOwnerMemberAsync(database, leavesTarget);
        var callback = new SaveBoundaryCallback();
        var probe = new NavigationWriteProbe();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback, failure, probe);
        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareMixedPayloadOwnerGraphAsync(context);
        var storedBefore = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(database);
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
            graph.A.Entry.Property(node => node.Name).IsModified = false;
        }

        graph.A.Note.CurrentValue = "following payload";
        var writesBeforeFollowup = probe.Writes;

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(database);

        // Assert
        Assert.Same(expectedError, firstError);
        Assert.DoesNotContain(graph.A.Node, nativeAfterFailure);
        Assert.Equal(leavesTarget, nativeAfterFailure.Contains(graph.B.Entity));
        Assert.Contains(graph.A.Node, graph.A.Target.Nodes);
        Assert.Equal(leavesTarget, graph.A.Target.Nodes.Contains(graph.B.Entity));

        if (detachRawMember)
        {
            Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
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

    /// <summary>Each newly installed raw native member is undone without removing pending CLR occurrences.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedPayloadOwnerRollbackPreservesRawMembersAndDuplicateClrOccurrences(bool duplicate)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedMixedPayloadOwnerMemberAsync(database, leavesTarget: false);

        await using (var setup = await CreateNavigationContextAsync(database, wrapped: true))
        {
            await setup.NestedSet<NavigationNode>().ForScope(1)
                .InsertChildAsync(new NavigationNode { Id = 4, Name = "Charlie", PayloadOwnerId = 1 }, 1,
                    CancellationToken.None);
        }

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback, failure);
        var graph = await PrepareMixedPayloadOwnerGraphAsync(context);
        var c = await context.Set<NavigationNode>().SingleAsync(node => node.Id == 4, CancellationToken.None);
        var cEntry = context.Entry(c);
        var cForeignKey = cEntry.Property(node => node.PayloadOwnerId);
        var before = CapturePlannedParentState(context);
        var nativeBefore = PayloadOwnerNativeMembers(context, graph.A.Target);
        var previousNative = PayloadOwnerNativeMembers(context, graph.A.Previous);
        var collection = graph.A.Target.Nodes;
        NavigationNode[] pending = duplicate ? [graph.A.Node, c, graph.A.Node] : [graph.A.Node, c];
        var storedBefore = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(database);
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
        var storedAfter = await ReadPreparedOwnerStoreAsync(database);
        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(database);

        // Assert
        Assert.Same(failure.Error, error);
        AssertPlannedParentState(before.Where(entry => entry.Entity is NavigationNode).ToArray(),
            after.Where(entry => entry.Entity is NavigationNode).ToArray());

        Assert.Same(collection, graph.A.Target.Nodes);
        Assert.Equal(pending, collection, ReferenceEqualityComparer.Instance);
        Assert.True(nativeBefore.ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(nativeAfter));
        Assert.True(previousNative.ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(previousNativeAfter));
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

    /// <summary>Seeds B's original relationship independently of the callback whose rollback is tested.</summary>
    private static async Task SeedMixedPayloadOwnerMemberAsync(TestDatabase database, bool leavesTarget)
    {
        await SeedPayloadOwnersAsync(database, assigned: true);
        await using var setup = await CreateNavigationContextAsync(database, wrapped: true);
        var owner = leavesTarget ? 2 : 1;
        await setup.Set<NavigationNode>().Where(node => node.Id == 3)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.PayloadOwnerId, (int?)owner),
                CancellationToken.None);
    }

    /// <summary>Retains both existing entries before a callback mixes raw assignment with public EF fixup.</summary>
    private static async Task<MixedPayloadOwnerGraph> PrepareMixedPayloadOwnerGraphAsync(DbContext context)
    {
        var a = await PreparePayloadOwnerRecoveryAsync(context);
        var b = await context.Set<NavigationNode>().SingleAsync(node => node.Id == 3, CancellationToken.None);
        var entry = context.Entry(b);

        return new MixedPayloadOwnerGraph(a, entry, entry.Property(node => node.PayloadOwnerId));
    }

    /// <summary>Changes B through real fixup before A's prepared raw membership is installed by the library.</summary>
    private static void StageMixedPayloadOwnerWrites(MixedPayloadOwnerGraph graph, bool leavesTarget)
    {
        graph.BForeignKey.CurrentValue = leavesTarget ? graph.A.Previous.Id : graph.A.Target.Id;
        StagePayloadOwnerWrite(graph.A, publicCurrentValue: false);
    }

    /// <summary>Reads participating stored FK values independently of the restored tracker or CLR graph.</summary>
    private static async Task<MixedPayloadOwnerAssignment[]> ReadMixedPayloadOwnerAssignmentsAsync(
        TestDatabase database
    )
    {
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);

        return await verification.Set<NavigationNode>().Where(node => node.Id >= 2 && node.Id <= 4)
            .OrderBy(node => node.Id).Select(node => new MixedPayloadOwnerAssignment(node.Id, node.PayloadOwnerId))
            .ToArrayAsync(CancellationToken.None);
    }

    /// <summary>Holds exact tracked objects and property entries for two independent application writes.</summary>
    private sealed record MixedPayloadOwnerGraph(
        PayloadOwnerRecoveryGraph A,
        EntityEntry<NavigationNode> B,
        PropertyEntry<NavigationNode, int?> BForeignKey);

    /// <summary>Captures persisted relationship identity without materializing tracked entities.</summary>
    private sealed record MixedPayloadOwnerAssignment(int Id, int? PayloadOwnerId);
}
