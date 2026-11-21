using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>A callback FK write cannot leave a saved native baseline installed after rollback.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveChangesAsync_PayloadOwnerCallbackRollback_RestoresInstalledNativeBaseline(
        bool publicCurrentValue,
        bool hierarchyFailure
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPayloadOwnersAsync(
            database,
            assigned: true,
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
        var graph = await PreparePayloadOwnerRecoveryAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var nodeBefore = CapturePlannedParentState(context)
            .Single(entry => ReferenceEquals(entry.Entity, graph.Node));

        var membersBefore = PayloadOwnerNativeMembers(context, graph.Target);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StagePayloadOwnerWrite(graph, publicCurrentValue);
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var nodeAfter = CapturePlannedParentState(context)
            .Single(entry => ReferenceEquals(entry.Entity, graph.Node));

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
        Assert.Same(expectedError, error);
        AssertPlannedParentState([nodeBefore], [nodeAfter]);
        Assert.True(
            membersBefore
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(membersAfter));

        Assert.DoesNotContain(graph.Node, membersAfter);

        // WHY: Raw CLR membership remains pending; public FK assignment participates in EF's rollback fixup.
        Assert.Equal(!publicCurrentValue, graph.Target.Nodes.Contains(graph.Node));
        Assert.Equal(1, graph.Node.PayloadOwnerId);
        Assert.Same(graph.Previous, graph.Node.PayloadOwner);
        Assert.False(graph.ForeignKey.IsModified);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(1, storedOwner);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Independent retry policies preserve pending raw membership without detecting it implicitly.</summary>
    [Theory]
    [InlineData(false, "automatic")]
    [InlineData(false, "manual-unmarked")]
    [InlineData(false, "manual-marked")]
    [InlineData(true, "automatic")]
    [InlineData(true, "manual-unmarked")]
    [InlineData(true, "manual-marked")]
    public async Task SaveChangesAsync_PayloadOwnerCallbackRollback_AllowsIndependentRetry(
        bool hierarchyFailure,
        string retryPolicy
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPayloadOwnersAsync(
            database,
            assigned: true,
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
        var graph = await PreparePayloadOwnerRecoveryAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StagePayloadOwnerWrite(graph, publicCurrentValue: false);
        };

        // WHY: The first rollback is the retry's precondition; neither fault injector throws a second time.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var failedMembers = PayloadOwnerNativeMembers(context, graph.Target);
        var pendingMembership = graph.Target.Nodes.Contains(graph.Node);
        var restoredForeignKey = graph.Node.PayloadOwnerId;
        var restoredModified = graph.ForeignKey.IsModified;
        context.ChangeTracker.AutoDetectChangesEnabled = retryPolicy == "automatic";

        if (retryPolicy == "manual-marked")
        {
            // WHY: Manual retry must explicitly mark its desired FK rather than converting CLR membership to a write.
            graph.Node.PayloadOwnerId = graph.Target.Id;
            graph.ForeignKey.IsModified = true;
        }

        // Act
        var retryError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var savedMembers = PayloadOwnerNativeMembers(context, graph.Target);
        var stored = await ReadPreparedOwnerStoreAsync(
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
        Assert.Same(expectedError, firstError);
        Assert.DoesNotContain(graph.Node, failedMembers);
        Assert.True(pendingMembership);
        Assert.Equal(1, restoredForeignKey);
        Assert.False(restoredModified);
        Assert.Null(retryError);
        var persistsRelationship = retryPolicy != "manual-unmarked";
        Assert.Equal(
            persistsRelationship
                ? 2
                : 1,
            storedOwner);

        Assert.Equal(persistsRelationship, savedMembers.Contains(graph.Node));
        Assert.Contains(graph.Node, graph.Target.Nodes);
        Assert.Equal(
            "Zulu",
            stored.Nodes.Single(node => node.Id == graph.Node.Id)
                .Name);

        Assert.Equal(0, stored.Issues);
        Assert.Equal(retryPolicy == "automatic", context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A rolled-back membership cannot authorize an ordinary save after its node is detached.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_PayloadOwnerCallbackRollback_RejectsDetachedFollowup(
        bool hierarchyFailure
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPayloadOwnersAsync(
            database,
            assigned: true,
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
        var graph = await PreparePayloadOwnerRecoveryAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StagePayloadOwnerWrite(graph, publicCurrentValue: false);
        };

        // WHY: Detachment follows a failed save, so no later write can rely on that operation's native baseline.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var failedMembers = PayloadOwnerNativeMembers(context, graph.Target);
        graph.Entry.State = EntityState.Detached;
        graph.Note.CurrentValue = "following payload";
        var writesBeforeFollowup = probe.Writes;

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
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
        Assert.Same(expectedError, firstError);
        Assert.DoesNotContain(graph.Node, failedMembers);
        Assert.Contains(graph.Node, graph.Target.Nodes);
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(error)
                .Code);

        Assert.Equal(writesBeforeFollowup, probe.Writes);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(1, storedOwner);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Copies installed native membership independently of the pending application collection.</summary>
    private static object[] PayloadOwnerNativeMembers(
        DbContext context,
        NavigationPayloadOwner owner
    )
    {
        var entry = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Single(value => ReferenceEquals(
                value.ToEntityEntry()
                    .Entity,
                owner));

        var navigation = entry.EntityType.FindNavigation(nameof(NavigationPayloadOwner.Nodes))!;

        return (entry.GetRelationshipSnapshotValue(navigation) as IEnumerable<object> ?? []).ToArray();
    }

    /// <summary>Expresses the two distinct application FK write paths without running change detection.</summary>
    private static void StagePayloadOwnerWrite(
        PayloadOwnerRecoveryGraph graph,
        bool publicCurrentValue
    )
    {
        if (publicCurrentValue)
        {
            graph.ForeignKey.CurrentValue = graph.Target.Id;
        }
        else
        {
            graph.Node.PayloadOwnerId = graph.Target.Id;
            graph.ForeignKey.IsModified = true;
        }

        if (!graph.Target.Nodes.Contains(graph.Node))
        {
            graph.Target.Nodes.Add(graph.Node);
        }
    }

    /// <summary>Arms a structural fault only after the provider's ordinary payload save completed.</summary>
    private static void ArmPayloadOwnerHierarchyFailure(
        DbContext context,
        IInterceptor interceptor
    )
    {
        if (interceptor is PayloadOwnerHierarchyFailure failure)
        {
            context.SavedChanges += (_, _) => failure.Arm();
        }
    }
}
