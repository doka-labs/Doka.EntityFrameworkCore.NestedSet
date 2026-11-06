namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Provider-promoted peers recover exactly without replacing the original save exception.</summary>
    [Theory]
    [InlineData("none", "concurrency", false)]
    [InlineData("owned", "concurrency", false)]
    [InlineData("split", "concurrency", false)]
    [InlineData("none", "completion", false)]
    [InlineData("owned", "completion", false)]
    [InlineData("split", "completion", false)]
    [InlineData("none", "hierarchy", false)]
    [InlineData("owned", "hierarchy", false)]
    [InlineData("split", "hierarchy", false)]
    [InlineData("none", "completion", true)]
    [InlineData("owned", "completion", true)]
    [InlineData("split", "completion", true)]
    public async Task TableSharingRollbackRestoresUnchangedPeer(
        string shape,
        string fault,
        bool rawUnmarkedPayload
    )
    {
        // Arrange
        await using var database = await CreateTableSharingRecoveryDatabaseAsync(
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new NavigationWriteProbe();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(fault == "hierarchy");
        IInterceptor[] interceptors = fault == "concurrency"
            ? [probe]
            : [probe, failure];

        await using var context = await CreateTableSharingRecoveryContextAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken,
            interceptors);

        if (fault == "hierarchy")
        {
            ArmPayloadOwnerHierarchyFailure(context, failure);
        }

        var graph = await PrepareTableSharingRecoveryAsync(
            context,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        if (rawUnmarkedPayload)
        {
            // WHY: State restoration must preserve a captured Current/Original difference without accepting it.
            ChangeUnmarkedTableSharingPayload(graph, shape);
        }

        var peerState = graph.Peer?.State;
        var peerFlags = graph
            .Peer
            ?.Properties
            .Select(property => property.IsModified)
            .ToArray();

        var before = CapturePlannedParentState(context);

        if (fault == "concurrency")
        {
            // WHY: All tracked sharing entries retain consistent original tokens; only the stored row advances.
            await ChangeTableSharingStoredVersionAsync(
                database,
                shape,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var storedBefore = await ReadTableSharingRecoveryStoreAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var storedAfter = await ReadTableSharingRecoveryStoreAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        if (shape == "none")
        {
            Assert.Null(peerState);
            Assert.Null(peerFlags);
        }
        else
        {
            Assert.Equal(EntityState.Unchanged, peerState);
            Assert.NotNull(peerFlags);
            Assert.All(peerFlags, flag => Assert.False(flag));
        }

        AssertPlannedParentState(before, after);

        if (fault == "concurrency")
        {
            Assert.IsType<DbUpdateConcurrencyException>(error);
        }
        else
        {
            Assert.Same(expectedError, error);
        }

        Assert.True(probe.Writes > 0);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Payloads, storedAfter.Payloads);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A failed shared-row save retries without manually repairing any entry state or property flag.</summary>
    [Theory]
    [InlineData("owned", false)]
    [InlineData("owned", true)]
    [InlineData("split", false)]
    [InlineData("split", true)]
    public async Task TableSharingRollbackAllowsIdenticalRetry(
        string shape,
        bool hierarchyFailure
    )
    {
        // Arrange
        await using var database = await CreateTableSharingRecoveryDatabaseAsync(
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateTableSharingRecoveryContextAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareTableSharingRecoveryAsync(
            context,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var before = CapturePlannedParentState(context);
        var storedBefore = await ReadTableSharingRecoveryStoreAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // WHY: The failed save is the retry's precondition; both injected faults are one-shot observers.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var afterFailure = CapturePlannedParentState(context);

        // Act
        var retryError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadTableSharingRecoveryStoreAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertPlannedParentState(before, afterFailure);
        Assert.Same(expectedError, firstError);
        Assert.Null(retryError);
        Assert.NotNull(graph.Peer);
        Assert.Equal(EntityState.Unchanged, graph.Peer.State);
        Assert.All(graph.Peer.Properties, property => Assert.False(property.IsModified));
        Assert.Equal(EntityState.Unchanged, graph.Entry.State);
        Assert.Equal(
            "Zulu",
            storedAfter.Nodes.Single(node => node.Id == graph.Node.Id)
                .Name);

        Assert.All(storedAfter.Nodes, node => Assert.Equal(7, node.Version));
        Assert.Equal(storedBefore.Payloads, storedAfter.Payloads);
        Assert.Equal(0, storedAfter.Issues);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Successful coordination persists only marked values despite provider sharing-entry promotion.</summary>
    [Theory]
    [InlineData("none", false)]
    [InlineData("none", true)]
    [InlineData("owned", false)]
    [InlineData("owned", true)]
    [InlineData("split", false)]
    [InlineData("split", true)]
    public async Task TableSharingSuccessfulSavePreservesUnmarkedPayload(
        string shape,
        bool rawUnmarkedPayload
    )
    {
        // Arrange
        await using var database = await CreateTableSharingRecoveryDatabaseAsync(
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = await CreateTableSharingRecoveryContextAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var graph = await PrepareTableSharingRecoveryAsync(
            context,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        if (rawUnmarkedPayload)
        {
            ChangeUnmarkedTableSharingPayload(graph, shape);
        }

        var peerStateBefore = graph.Peer?.State;
        var payloadMarked = shape == "none"
            ? graph.Entry.Property(node => node.Payload)
                .IsModified
            : graph.Peer!.Property("Value")
                .IsModified;

        var storedBefore = await ReadTableSharingRecoveryStoreAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var currentPayloadBefore = CurrentTableSharingPayload(graph, shape);
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var storedAfter = await ReadTableSharingRecoveryStoreAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(payloadMarked);
        Assert.Equal(EntityState.Unchanged, graph.Entry.State);

        if (shape == "none")
        {
            Assert.Null(peerStateBefore);
        }
        else
        {
            Assert.Equal(EntityState.Unchanged, peerStateBefore);
            Assert.NotNull(graph.Peer);
            Assert.Equal(EntityState.Unchanged, graph.Peer.State);
            Assert.All(graph.Peer.Properties, property => Assert.False(property.IsModified));
        }

        Assert.Equal(currentPayloadBefore, CurrentTableSharingPayload(graph, shape));
        Assert.Equal(
            "Zulu",
            storedAfter.Nodes.Single(node => node.Id == graph.Node.Id)
                .Name);

        Assert.Equal(
            storedBefore.Nodes.Single(node => node.Id == graph.Node.Id)
                .Payload,
            storedAfter.Nodes.Single(node => node.Id == graph.Node.Id)
                .Payload);

        Assert.Equal(storedBefore.Payloads, storedAfter.Payloads);
        Assert.All(storedAfter.Nodes, node => Assert.Equal(7, node.Version));
        Assert.Equal(0, storedAfter.Issues);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
